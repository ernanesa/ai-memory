using AiMemory.Models;
using Npgsql;
using Pgvector;

namespace AiMemory.Services;

public sealed class PgVectorService(string connectionString)
{
    public async Task UpsertChunkAsync(CodeChunk chunk, float[] embedding, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        WITH project AS (
            INSERT INTO ai_projects(name, root_path)
            VALUES ($1, $2)
            ON CONFLICT(name) DO UPDATE SET root_path = EXCLUDED.root_path
            RETURNING id
        )
        INSERT INTO ai_chunks(project_id, file_path, language, chunk_type, symbol_name, content, content_hash, embedding, updated_at)
        SELECT id, $3, $4, $5, $6, $7, $8, $9, NOW()
        FROM project
        ON CONFLICT(project_id, file_path, content_hash)
        DO UPDATE SET content = EXCLUDED.content, embedding = EXCLUDED.embedding, updated_at = NOW();
        """;
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

    public async Task<IReadOnlyList<(string Project, string File, string? Symbol, string Content, double Distance)>> SearchAsync(float[] embedding, int limit, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT p.name, c.file_path, c.symbol_name, c.content, c.embedding <=> $1 AS distance
        FROM ai_chunks c
        JOIN ai_projects p ON p.id = c.project_id
        ORDER BY c.embedding <=> $1
        LIMIT $2;
        """;
        cmd.Parameters.AddWithValue(new Vector(embedding));
        cmd.Parameters.AddWithValue(limit);
        var rows = new List<(string, string, string?, string, double)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.GetDouble(4)));
        return rows;
    }
}
