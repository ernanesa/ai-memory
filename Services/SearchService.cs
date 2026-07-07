using Npgsql;
using Pgvector;

namespace AiMemory.Services
{
    public sealed class SearchService
    {
        private readonly NpgsqlDataSource _dataSource;

        public SearchService(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<IReadOnlyList<(string Project, string File, string? Symbol, string Content, double Distance)>> SearchAsync(float[] embedding, string query, int limit, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        WITH vector_search AS (
            SELECT c.id,
                   row_number() OVER (ORDER BY c.embedding <=> $1) as rank
            FROM ai_chunks c
            ORDER BY c.embedding <=> $1
            LIMIT GREATEST($2 * 10, 100)
        ),
        text_search AS (
            SELECT c.id,
                   row_number() OVER (ORDER BY ts_rank_cd(c.search_vector, websearch_to_tsquery('english', $3)) DESC) as rank
            FROM ai_chunks c
            WHERE c.search_vector @@ websearch_to_tsquery('english', $3)
            ORDER BY ts_rank_cd(c.search_vector, websearch_to_tsquery('english', $3)) DESC
            LIMIT GREATEST($2 * 10, 100)
        )
        SELECT p.name,
               c.file_path,
               c.symbol_name,
               c.content,
               coalesce(1.0 / (60.0 + v.rank), 0.0) + coalesce(1.0 / (60.0 + t.rank), 0.0) as rrf_score
        FROM ai_chunks c
        JOIN ai_projects p ON p.id = c.project_id
        LEFT JOIN vector_search v ON v.id = c.id
        LEFT JOIN text_search t ON t.id = c.id
        WHERE v.id IS NOT NULL OR t.id IS NOT NULL
        ORDER BY rrf_score DESC
        LIMIT $2;
        """;
            cmd.Parameters.AddWithValue(new Vector(embedding));
            cmd.Parameters.AddWithValue(limit);
            cmd.Parameters.AddWithValue(query.Trim());

            var rows = new List<(string, string, string?, string, double)>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetDouble(4)));
            }
            return rows;
        }

        public async Task<IReadOnlyList<CodeSearchResult>> SearchCodeAsync(float[] embedding, string query, int limit, string? project, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        WITH vector_search AS (
            SELECT c.id,
                   row_number() OVER (ORDER BY c.embedding <=> $1) as rank
            FROM ai_chunks c
            JOIN ai_projects p ON p.id = c.project_id
            WHERE c.embedding IS NOT NULL
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
            ORDER BY c.embedding <=> $1
            LIMIT GREATEST($2 * 10, 100)
        ),
        text_search AS (
            SELECT c.id,
                   row_number() OVER (ORDER BY ts_rank_cd(c.search_vector, websearch_to_tsquery('english', $4)) DESC) as rank
            FROM ai_chunks c
            JOIN ai_projects p ON p.id = c.project_id
            WHERE c.search_vector @@ websearch_to_tsquery('english', $4)
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
            ORDER BY ts_rank_cd(c.search_vector, websearch_to_tsquery('english', $4)) DESC
            LIMIT GREATEST($2 * 10, 100)
        )
        SELECT p.name,
               c.file_path,
               c.language,
               c.chunk_type,
               c.symbol_name,
               c.content,
               coalesce(1.0 / (60.0 + v.rank), 0.0) + coalesce(1.0 / (60.0 + t.rank), 0.0) as rrf_score
        FROM ai_chunks c
        JOIN ai_projects p ON p.id = c.project_id
        LEFT JOIN vector_search v ON v.id = c.id
        LEFT JOIN text_search t ON t.id = c.id
        WHERE v.id IS NOT NULL OR t.id IS NOT NULL
        ORDER BY rrf_score DESC
        LIMIT $2;
        """;
            cmd.Parameters.AddWithValue(new Vector(embedding));
            cmd.Parameters.AddWithValue(limit);
            cmd.Parameters.AddWithValue((object?)SqlPredicates.NormalizeFilter(project) ?? DBNull.Value);
            cmd.Parameters.AddWithValue(query.Trim());

            var rows = new List<CodeSearchResult>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new CodeSearchResult(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.GetDouble(6)));
            }

            return rows;
        }

        public async Task<IReadOnlyList<RelatedFileResult>> FindRelatedFilesAsync(float[] embedding, int limit, string? project, string? excludeFile, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        SELECT p.name,
               c.file_path,
               min(c.embedding <=> $1) AS distance,
               count(*)::int AS matched_chunks,
               string_agg(DISTINCT c.symbol_name, ', ' ORDER BY c.symbol_name) FILTER (WHERE c.symbol_name IS NOT NULL) AS symbols
        FROM ai_chunks c
        JOIN ai_projects p ON p.id = c.project_id
        WHERE c.embedding IS NOT NULL
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
          AND ($4::text IS NULL OR (c.file_path <> $4 AND c.file_path NOT ILIKE '%' || $4))
        GROUP BY p.name, c.file_path
        ORDER BY min(c.embedding <=> $1)
        LIMIT $2;
        """;
            cmd.Parameters.AddWithValue(new Vector(embedding));
            cmd.Parameters.AddWithValue(limit);
            cmd.Parameters.AddWithValue((object?)SqlPredicates.NormalizeFilter(project) ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)SqlPredicates.NormalizeFilter(excludeFile) ?? DBNull.Value);

            var rows = new List<RelatedFileResult>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new RelatedFileResult(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetDouble(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

            return rows;
        }
    }
}
