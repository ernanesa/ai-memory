using Npgsql;
using Pgvector;

namespace AiMemory.Services
{
    public sealed class KnowledgeRepository
    {
        private readonly NpgsqlDataSource _dataSource;

        public KnowledgeRepository(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<ExtractionStats> GetKnowledgeExtractionStatsAsync(string workspace, IReadOnlyList<string> projects, bool semantic, CancellationToken ct = default)
        {
            return await GetExtractionStatsAsync(workspace, projects, "knowledge", semantic ? "TRUE" : SqlPredicates.KnowledgeCandidatePredicate, ct);
        }

        public async Task<IReadOnlyList<ExtractionChunkResult>> GetChunksForKnowledgeExtractionAsync(string workspace, IReadOnlyList<string> projects, int? limit, bool semantic, bool refresh, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            var predicate = semantic ? "TRUE" : SqlPredicates.KnowledgeCandidatePredicate;
            var indexableFilePredicate = SqlPredicates.IndexableFilePredicate("c", "p");
            var statePredicate = refresh
                ? "TRUE"
                : "(s.id IS NULL OR s.content_hash <> c.content_hash OR s.status = 'failed')";
            cmd.CommandText = $"""
        SELECT c.id, p.name, c.file_path, c.language, c.chunk_type, c.symbol_name, c.content, c.content_hash
        FROM ai_chunks c
        JOIN ai_projects p ON p.id = c.project_id
        JOIN ai_workspace_projects wp ON wp.project_id = p.id
        JOIN ai_workspaces w ON w.id = wp.workspace_id
        LEFT JOIN ai_extraction_chunk_state s ON s.chunk_id = c.id AND s.stage = 'knowledge'
        WHERE w.name = $1
          AND (cardinality($2::text[]) = 0 OR p.name = ANY($2))
          AND ({indexableFilePredicate})
          AND ({predicate})
          AND {statePredicate}
        ORDER BY
          CASE
            WHEN s.status = 'failed' THEN 0
            WHEN s.id IS NULL OR s.content_hash <> c.content_hash THEN 1
            ELSE 2
          END,
          c.updated_at DESC
        {SqlPredicates.LimitClause(limit)};
        """;
            cmd.Parameters.AddWithValue(workspace);
            cmd.Parameters.AddWithValue(projects.ToArray());
            return await ReadExtractionChunksAsync(cmd, ct);
        }

        public async Task<UpsertExtractionResult> UpsertKnowledgeCandidateAsync(ExtractedKnowledge knowledge, float[] embedding, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        WITH chunk AS (
            SELECT id, project_id
            FROM ai_chunks
            WHERE id = $1
        ),
        updated AS (
            UPDATE ai_knowledge k
            SET kind = $2,
                title = $3,
                content = $4,
                source = $5,
                symbol_name = $6,
                evidence = $7,
                confidence = $8,
                embedding = $9,
                chunk_id = chunk.id,
                content_hash = $10,
                updated_at = NOW()
            FROM chunk
            WHERE k.project_id = chunk.project_id
              AND k.status <> 'rejected'
              AND (k.content_hash = $10 OR (lower(k.kind) = lower($2) AND lower(k.title) = lower($3)))
            RETURNING k.id, k.status, 'updated'::text AS action
        ),
        inserted AS (
            INSERT INTO ai_knowledge(project_id, chunk_id, kind, title, content, source, symbol_name, status, evidence, content_hash, confidence, embedding, updated_at)
            SELECT chunk.project_id, chunk.id, $2, $3, $4, $5, $6, 'candidate', $7, $10, $8, $9, NOW()
            FROM chunk
            WHERE NOT EXISTS (SELECT 1 FROM updated)
            RETURNING id, status, 'inserted'::text AS action
        )
        SELECT id, status, action FROM updated
        UNION ALL
        SELECT id, status, action FROM inserted
        LIMIT 1;
        """;
            cmd.Parameters.AddWithValue(knowledge.ChunkId);
            cmd.Parameters.AddWithValue(knowledge.Kind);
            cmd.Parameters.AddWithValue(knowledge.Title);
            cmd.Parameters.AddWithValue(knowledge.Content);
            cmd.Parameters.AddWithValue((object?)knowledge.Source ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)knowledge.SymbolName ?? DBNull.Value);
            cmd.Parameters.AddWithValue(knowledge.Evidence);
            cmd.Parameters.AddWithValue(knowledge.Confidence);
            cmd.Parameters.AddWithValue(new Vector(embedding));
            cmd.Parameters.AddWithValue(knowledge.ContentHash);
            return await ReadUpsertResultAsync(cmd, ct);
        }

        public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchKnowledgeAsync(float[] embedding, string query, int limit, string? project, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        WITH vector_search AS (
            SELECT k.id,
                   row_number() OVER (ORDER BY k.embedding <=> $1) as rank
            FROM ai_knowledge k
            LEFT JOIN ai_projects p ON p.id = k.project_id
            WHERE k.embedding IS NOT NULL
              AND k.status <> 'rejected'
              AND (
                  $3::text IS NULL
                  OR p.name = $3
                  OR EXISTS (
                      SELECT 1
                      FROM ai_workspace_projects wp
                      JOIN ai_workspaces w ON w.id = wp.workspace_id
                      WHERE wp.project_id = p.id
                        AND (w.name = $3 OR w.name || '/' || p.name = $3)
                  )
              )
            ORDER BY k.embedding <=> $1
            LIMIT GREATEST($2 * 10, 100)
        ),
        text_search AS (
            SELECT k.id,
                   row_number() OVER (ORDER BY ts_rank_cd(k.search_vector, websearch_to_tsquery('portuguese', $4)) DESC) as rank
            FROM ai_knowledge k
            LEFT JOIN ai_projects p ON p.id = k.project_id
            WHERE k.search_vector @@ websearch_to_tsquery('portuguese', $4)
              AND k.status <> 'rejected'
              AND (
                  $3::text IS NULL
                  OR p.name = $3
                  OR EXISTS (
                      SELECT 1
                      FROM ai_workspace_projects wp
                      JOIN ai_workspaces w ON w.id = wp.workspace_id
                      WHERE wp.project_id = p.id
                        AND (w.name = $3 OR w.name || '/' || p.name = $3)
                  )
              )
            ORDER BY ts_rank_cd(k.search_vector, websearch_to_tsquery('portuguese', $4)) DESC
            LIMIT GREATEST($2 * 10, 100)
        )
        SELECT p.name,
               k.kind,
               k.title,
               k.content,
               k.source,
               k.symbol_name,
               k.status,
               k.evidence,
               k.confidence,
               coalesce(1.0 / (60.0 + v.rank), 0.0) + coalesce(1.0 / (60.0 + t.rank), 0.0) as rrf_score
        FROM ai_knowledge k
        LEFT JOIN ai_projects p ON p.id = k.project_id
        LEFT JOIN vector_search v ON v.id = k.id
        LEFT JOIN text_search t ON t.id = k.id
        WHERE v.id IS NOT NULL OR t.id IS NOT NULL
        ORDER BY rrf_score DESC
        LIMIT $2;
        """;
            cmd.Parameters.AddWithValue(new Vector(embedding));
            cmd.Parameters.AddWithValue(limit);
            cmd.Parameters.AddWithValue((object?)SqlPredicates.NormalizeFilter(project) ?? DBNull.Value);
            cmd.Parameters.AddWithValue(query.Trim());

            var rows = new List<KnowledgeSearchResult>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new KnowledgeSearchResult(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                    reader.GetDouble(9)));
            }

            return rows;
        }

        private async Task<ExtractionStats> GetExtractionStatsAsync(string workspace, IReadOnlyList<string> projects, string stage, string candidatePredicate, CancellationToken ct)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            var indexableFilePredicate = SqlPredicates.IndexableFilePredicate("c", "p");
            cmd.CommandText = $"""
        SELECT count(*) AS total_chunks,
               count(*) FILTER (WHERE {candidatePredicate}) AS candidate_chunks,
               count(*) FILTER (
                   WHERE ({candidatePredicate})
                     AND s.status = 'processed'
                     AND s.content_hash = c.content_hash
               ) AS processed_candidate_chunks,
               count(*) FILTER (
                   WHERE ({candidatePredicate})
                     AND s.id IS NULL
               ) AS pending_candidate_chunks,
               count(*) FILTER (
                   WHERE ({candidatePredicate})
                     AND s.status = 'failed'
                     AND s.content_hash = c.content_hash
               ) AS failed_candidate_chunks,
               count(*) FILTER (
                   WHERE ({candidatePredicate})
                     AND s.id IS NOT NULL
                     AND s.content_hash <> c.content_hash
               ) AS changed_candidate_chunks,
               count(*) FILTER (
                   WHERE ({candidatePredicate})
                     AND (s.id IS NULL OR s.content_hash <> c.content_hash OR s.status = 'failed')
               ) AS actionable_candidate_chunks
        FROM ai_chunks c
        JOIN ai_projects p ON p.id = c.project_id
        JOIN ai_workspace_projects wp ON wp.project_id = p.id
        JOIN ai_workspaces w ON w.id = wp.workspace_id
        LEFT JOIN ai_extraction_chunk_state s ON s.chunk_id = c.id AND s.stage = $3
        WHERE w.name = $1
          AND ({indexableFilePredicate})
          AND (cardinality($2::text[]) = 0 OR p.name = ANY($2));
        """;
            cmd.Parameters.AddWithValue(workspace);
            cmd.Parameters.AddWithValue(projects.ToArray());
            cmd.Parameters.AddWithValue(stage);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new ExtractionStats(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6));
        }

        private static async Task<IReadOnlyList<ExtractionChunkResult>> ReadExtractionChunksAsync(NpgsqlCommand cmd, CancellationToken ct)
        {
            var rows = new List<ExtractionChunkResult>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new ExtractionChunkResult(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7)));
            }

            return rows;
        }

        private static async Task<UpsertExtractionResult> ReadUpsertResultAsync(NpgsqlCommand cmd, CancellationToken ct)
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return new UpsertExtractionResult(null, null, "skipped");
            }

            return new UpsertExtractionResult(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
        }
    }
}
