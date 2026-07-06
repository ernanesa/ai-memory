using Npgsql;
using Pgvector;

namespace AiMemory.Services
{
    public sealed class RuleRepository
    {
        private readonly NpgsqlDataSource _dataSource;

        public RuleRepository(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<ExtractionStats> GetRuleExtractionStatsAsync(string workspace, IReadOnlyList<string> projects, bool semantic, CancellationToken ct = default)
        {
            return await GetExtractionStatsAsync(workspace, projects, "rules", semantic ? SqlPredicates.SemanticRuleCandidatePredicate : SqlPredicates.RuleCandidatePredicate, ct);
        }

        public async Task<IReadOnlyList<ExtractionChunkResult>> GetChunksForRuleExtractionAsync(string workspace, IReadOnlyList<string> projects, int? limit, bool semantic, bool refresh, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            var predicate = semantic ? SqlPredicates.SemanticRuleCandidatePredicate : SqlPredicates.RuleCandidatePredicate;
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
        LEFT JOIN ai_extraction_chunk_state s ON s.chunk_id = c.id AND s.stage = 'rules'
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

        public async Task<UpsertExtractionResult> UpsertBusinessRuleCandidateAsync(ExtractedBusinessRule rule, float[] embedding, CancellationToken ct = default)
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
            UPDATE ai_business_rules r
            SET title = $2,
                description = $3,
                source_file = $4,
                symbol_name = $5,
                evidence = $6,
                confidence = $7,
                embedding = $8,
                chunk_id = chunk.id,
                content_hash = $9,
                updated_at = NOW()
            FROM chunk
            WHERE r.project_id = chunk.project_id
              AND r.status <> 'rejected'
              AND (r.content_hash = $9 OR lower(r.title) = lower($2))
            RETURNING r.id, r.status, 'updated'::text AS action
        ),
        inserted AS (
            INSERT INTO ai_business_rules(project_id, chunk_id, title, description, source_file, symbol_name, status, evidence, content_hash, confidence, embedding, updated_at)
            SELECT chunk.project_id, chunk.id, $2, $3, $4, $5, 'candidate', $6, $9, $7, $8, NOW()
            FROM chunk
            WHERE NOT EXISTS (SELECT 1 FROM updated)
            RETURNING id, status, 'inserted'::text AS action
        )
        SELECT id, status, action FROM updated
        UNION ALL
        SELECT id, status, action FROM inserted
        LIMIT 1;
        """;
            cmd.Parameters.AddWithValue(rule.ChunkId);
            cmd.Parameters.AddWithValue(rule.Title);
            cmd.Parameters.AddWithValue(rule.Description);
            cmd.Parameters.AddWithValue((object?)rule.SourceFile ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)rule.SymbolName ?? DBNull.Value);
            cmd.Parameters.AddWithValue(rule.Evidence);
            cmd.Parameters.AddWithValue(rule.Confidence);
            cmd.Parameters.AddWithValue(new Vector(embedding));
            cmd.Parameters.AddWithValue(rule.ContentHash);
            return await ReadUpsertResultAsync(cmd, ct);
        }

        public async Task<IReadOnlyList<BusinessRuleSearchResult>> SearchBusinessRulesAsync(float[] embedding, string query, int limit, string? project, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        WITH vector_search AS (
            SELECT r.id,
                   row_number() OVER (ORDER BY r.embedding <=> $1) as rank
            FROM ai_business_rules r
            LEFT JOIN ai_projects p ON p.id = r.project_id
            WHERE r.embedding IS NOT NULL
              AND r.status <> 'rejected'
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
            ORDER BY r.embedding <=> $1
            LIMIT GREATEST($2 * 10, 100)
        ),
        text_search AS (
            SELECT r.id,
                   row_number() OVER (ORDER BY ts_rank_cd(r.search_vector, websearch_to_tsquery('portuguese', $4)) DESC) as rank
            FROM ai_business_rules r
            LEFT JOIN ai_projects p ON p.id = r.project_id
            WHERE r.search_vector @@ websearch_to_tsquery('portuguese', $4)
              AND r.status <> 'rejected'
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
            ORDER BY ts_rank_cd(r.search_vector, websearch_to_tsquery('portuguese', $4)) DESC
            LIMIT GREATEST($2 * 10, 100)
        )
        SELECT p.name,
               r.title,
               r.description,
               r.source_file,
               r.symbol_name,
               r.status,
               r.evidence,
               r.confidence,
               coalesce(1.0 / (60.0 + v.rank), 0.0) + coalesce(1.0 / (60.0 + t.rank), 0.0) as rrf_score
        FROM ai_business_rules r
        LEFT JOIN ai_projects p ON p.id = r.project_id
        LEFT JOIN vector_search v ON v.id = r.id
        LEFT JOIN text_search t ON t.id = r.id
        WHERE v.id IS NOT NULL OR t.id IS NOT NULL
        ORDER BY rrf_score DESC
        LIMIT $2;
        """;
            cmd.Parameters.AddWithValue(new Vector(embedding));
            cmd.Parameters.AddWithValue(limit);
            cmd.Parameters.AddWithValue((object?)SqlPredicates.NormalizeFilter(project) ?? DBNull.Value);
            cmd.Parameters.AddWithValue(query.Trim());

            var rows = new List<BusinessRuleSearchResult>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new BusinessRuleSearchResult(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                    reader.GetDouble(8)));
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
