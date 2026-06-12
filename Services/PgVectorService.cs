using AiMemory.Models;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;

namespace AiMemory.Services;

public sealed class PgVectorService : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PgVectorService(string connectionString)
    {
        _dataSource = CreateDataSource(connectionString);
    }

    public async Task UpsertChunkAsync(CodeChunk chunk, float[] embedding, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
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
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
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

    public async Task<IReadOnlyList<CodeSearchResult>> SearchCodeAsync(float[] embedding, int limit, string? project, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT p.name, c.file_path, c.language, c.chunk_type, c.symbol_name, c.content, c.embedding <=> $1 AS distance
        FROM ai_chunks c
        JOIN ai_projects p ON p.id = c.project_id
        WHERE c.embedding IS NOT NULL
          AND ($3::text IS NULL OR p.name = $3 OR p.name ILIKE '%/' || $3)
        ORDER BY c.embedding <=> $1
        LIMIT $2;
        """;
        cmd.Parameters.AddWithValue(new Vector(embedding));
        cmd.Parameters.AddWithValue(limit);
        cmd.Parameters.AddWithValue((object?)NormalizeFilter(project) ?? DBNull.Value);

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

    public async Task<IReadOnlyList<BusinessRuleSearchResult>> SearchBusinessRulesAsync(float[] embedding, int limit, string? project, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT p.name, r.title, r.description, r.source_file, r.confidence, r.embedding <=> $1 AS distance
        FROM ai_business_rules r
        LEFT JOIN ai_projects p ON p.id = r.project_id
        WHERE r.embedding IS NOT NULL
          AND ($3::text IS NULL OR p.name = $3 OR p.name ILIKE '%/' || $3)
        ORDER BY r.embedding <=> $1
        LIMIT $2;
        """;
        cmd.Parameters.AddWithValue(new Vector(embedding));
        cmd.Parameters.AddWithValue(limit);
        cmd.Parameters.AddWithValue((object?)NormalizeFilter(project) ?? DBNull.Value);

        var rows = new List<BusinessRuleSearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new BusinessRuleSearchResult(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.GetDouble(5)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<FileChunkResult>> GetFileChunksAsync(string file, string? project, int maxTotalChars, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT p.name, c.file_path, c.content
        FROM ai_chunks c
        JOIN ai_projects p ON p.id = c.project_id
        WHERE ($2::text IS NULL OR p.name = $2 OR p.name ILIKE '%/' || $2)
          AND (c.file_path = $1 OR c.file_path ILIKE '%' || $1)
        ORDER BY length(c.content) DESC;
        """;
        cmd.Parameters.AddWithValue(file.Trim());
        cmd.Parameters.AddWithValue((object?)NormalizeFilter(project) ?? DBNull.Value);

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
          AND ($3::text IS NULL OR p.name = $3 OR p.name ILIKE '%/' || $3)
          AND ($4::text IS NULL OR (c.file_path <> $4 AND c.file_path NOT ILIKE '%' || $4))
        GROUP BY p.name, c.file_path
        ORDER BY min(c.embedding <=> $1)
        LIMIT $2;
        """;
        cmd.Parameters.AddWithValue(new Vector(embedding));
        cmd.Parameters.AddWithValue(limit);
        cmd.Parameters.AddWithValue((object?)NormalizeFilter(project) ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)NormalizeFilter(excludeFile) ?? DBNull.Value);

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

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        return builder.Build();
    }
}

public sealed record CodeSearchResult(
    string Project,
    string File,
    string? Language,
    string? ChunkType,
    string? Symbol,
    string Content,
    double Distance);

public sealed record BusinessRuleSearchResult(
    string? Project,
    string Title,
    string Description,
    string? SourceFile,
    decimal? Confidence,
    double Distance);

public sealed record FileChunkResult(
    string Project,
    string File,
    string Content);

public sealed record RelatedFileResult(
    string Project,
    string File,
    double Distance,
    int MatchedChunks,
    string? Symbols);
