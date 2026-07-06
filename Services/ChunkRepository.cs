using AiMemory.Models;
using Npgsql;
using Pgvector;

namespace AiMemory.Services
{
    public sealed class ChunkRepository
    {
        private readonly NpgsqlDataSource _dataSource;

        public ChunkRepository(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task UpsertChunkAsync(string workspaceName, CodeChunk chunk, float[] embedding, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        WITH workspace AS (
            INSERT INTO ai_workspaces(name)
            VALUES ($1)
            ON CONFLICT(name) DO UPDATE SET name = EXCLUDED.name
            RETURNING id
        ),
        project AS (
            INSERT INTO ai_projects(name, root_path)
            VALUES ($2, $3)
            ON CONFLICT(root_path) DO UPDATE SET name = EXCLUDED.name
            RETURNING id
        ),
        workspace_project AS (
            INSERT INTO ai_workspace_projects(workspace_id, project_id)
            SELECT workspace.id, project.id
            FROM workspace, project
            ON CONFLICT(workspace_id, project_id) DO NOTHING
        )
        INSERT INTO ai_chunks(project_id, file_path, language, chunk_type, symbol_name, content, content_hash, embedding, updated_at)
        SELECT id, $4, $5, $6, $7, $8, $9, $10, NOW()
        FROM project
        ON CONFLICT(project_id, file_path, content_hash)
        DO UPDATE SET content = EXCLUDED.content, embedding = EXCLUDED.embedding, updated_at = NOW();
        """;
            cmd.Parameters.AddWithValue(workspaceName);
            cmd.Parameters.AddWithValue(chunk.ProjectName);
            cmd.Parameters.AddWithValue(chunk.RootPath);
            cmd.Parameters.AddWithValue(chunk.FilePath);
            cmd.Parameters.AddWithValue((object?)chunk.Language ?? DBNull.Value);
            cmd.Parameters.AddWithValue(chunk.ChunkType);
            cmd.Parameters.AddWithValue((object?)chunk.SymbolName ?? DBNull.Value);
            cmd.Parameters.AddWithValue(chunk.Content);
            cmd.Parameters.AddWithValue(chunk.ContentHash);
            cmd.Parameters.AddWithValue(new Vector(embedding));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> DeleteEntityFrameworkMigrationChunksAsync(string workspace, IReadOnlyList<string> projects, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            var entityFrameworkMigrationFilePredicate = SqlPredicates.EntityFrameworkMigrationFilePredicate("c");
            cmd.CommandText = $"""
        DELETE FROM ai_chunks c
        USING ai_projects p, ai_workspace_projects wp, ai_workspaces w
        WHERE c.project_id = p.id
          AND wp.project_id = p.id
          AND w.id = wp.workspace_id
          AND w.name = $1
          AND (cardinality($2::text[]) = 0 OR p.name = ANY($2))
          AND ({entityFrameworkMigrationFilePredicate});
        """;
            cmd.Parameters.AddWithValue(workspace);
            cmd.Parameters.AddWithValue(projects.ToArray());
            return await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> DeleteTestChunksAsync(string workspace, IReadOnlyList<string> projects, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            var testFilePredicate = SqlPredicates.TestFilePredicate("c", "p");
            cmd.CommandText = $"""
        DELETE FROM ai_chunks c
        USING ai_projects p, ai_workspace_projects wp, ai_workspaces w
        WHERE c.project_id = p.id
          AND wp.project_id = p.id
          AND w.id = wp.workspace_id
          AND w.name = $1
          AND (cardinality($2::text[]) = 0 OR p.name = ANY($2))
          AND ({testFilePredicate});
        """;
            cmd.Parameters.AddWithValue(workspace);
            cmd.Parameters.AddWithValue(projects.ToArray());
            return await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> DeleteOrphanChunksAsync(string workspace, IReadOnlyList<string> projects, IReadOnlyList<string> currentFilePaths, CancellationToken ct = default)
        {
            if (currentFilePaths.Count == 0)
            {
                return 0;
            }

            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        DELETE FROM ai_chunks c
        USING ai_projects p, ai_workspace_projects wp, ai_workspaces w
        WHERE c.project_id = p.id
          AND wp.project_id = p.id
          AND w.id = wp.workspace_id
          AND w.name = $1
          AND (cardinality($2::text[]) = 0 OR p.name = ANY($2))
          AND NOT (c.file_path = ANY($3::text[]));
        """;
            cmd.Parameters.AddWithValue(workspace);
            cmd.Parameters.AddWithValue(projects.ToArray());
            cmd.Parameters.AddWithValue(currentFilePaths.ToArray());
            return await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<IReadOnlyList<FileChunkResult>> GetFileChunksAsync(string file, string? project, int maxTotalChars, CancellationToken ct = default)
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
        SELECT p.name, c.file_path, c.content
        FROM ai_chunks c
        JOIN ai_projects p ON p.id = c.project_id
        WHERE (
              $2::text IS NULL
              OR p.name = $2
              OR EXISTS (
                  SELECT 1
                  FROM ai_workspace_projects wp
                  JOIN ai_workspaces w ON w.id = wp.workspace_id
                  WHERE wp.project_id = p.id
                    AND (w.name = $2 OR w.name || '/' || p.name = $2)
              )
          )
          AND (c.file_path = $1 OR c.file_path ILIKE '%' || $1)
        ORDER BY length(c.content) DESC;
        """;
            cmd.Parameters.AddWithValue(file.Trim());
            cmd.Parameters.AddWithValue((object?)SqlPredicates.NormalizeFilter(project) ?? DBNull.Value);

            var rows = new List<FileChunkResult>();
            var totalChars = 0;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct) && totalChars < maxTotalChars)
            {
                var content = reader.GetString(2);
                if (totalChars + content.Length > maxTotalChars)
                {
                    content = content[..Math.Max(0, maxTotalChars - totalChars)];
                }

                totalChars += content.Length;
                rows.Add(new FileChunkResult(reader.GetString(0), reader.GetString(1), content));
            }

            return rows;
        }
    }
}
