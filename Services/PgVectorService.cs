using AiMemory.Models;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;

namespace AiMemory.Services;

public sealed class PgVectorService : IAsyncDisposable
{
    private const string RuleCandidatePredicate = """
    c.content ILIKE '%throw new%'
    OR c.content ILIKE '%BusinessException%'
    OR c.content ILIKE '%ValidationException%'
    OR c.content ILIKE '%não pode%'
    OR c.content ILIKE '%nao pode%'
    OR c.content ILIKE '%deve%'
    OR c.content ILIKE '%obrigatório%'
    OR c.content ILIKE '%obrigatorio%'
    OR c.content ILIKE '%inválido%'
    OR c.content ILIKE '%invalido%'
    OR c.content ILIKE '%ErroContext%'
    OR c.content ILIKE '%ErrosContext%'
    OR c.content ILIKE '%ErroBase%'
    OR c.content ILIKE '%AdicionarErro%'
    OR c.content ILIKE '%AdicionaErro%'
    OR c.content ILIKE '%AddErro%'
    OR c.content ILIKE '%AddError%'
    OR c.content ILIKE '%AddFailure%'
    OR c.content ILIKE '%AddNotification%'
    OR c.content ILIKE '%Notificar%'
    OR c.content ILIKE '%Notification%'
    OR c.content ILIKE '%TemErro%'
    OR c.content ILIKE '%HasError%'
    OR c.content ILIKE '%IsValid%'
    OR c.content ILIKE '%Validate%'
    OR c.content ILIKE '%Validator%'
    OR c.content ILIKE '%RuleFor%'
    OR c.content ILIKE '%Elegivel%'
    OR c.content ILIKE '%Elegível%'
    OR c.content ILIKE '%Permite%'
    OR c.content ILIKE '%Bloqueado%'
    OR c.content ILIKE '%Cancelado%'
    OR c.content ILIKE '%Vencido%'
    """;

    private const string KnowledgeCandidatePredicate = """
    c.file_path ILIKE '%.csproj'
    OR c.file_path ILIKE '%Program.cs'
    OR c.file_path ILIKE '%Startup.cs'
    OR c.file_path ILIKE '%appsettings%'
    OR c.content ILIKE '%HttpClient%'
    OR c.content ILIKE '%MassTransit%'
    OR c.content ILIKE '%RabbitMQ%'
    OR c.content ILIKE '%Kafka%'
    OR c.content ILIKE '%MediatR%'
    OR c.content ILIKE '%EntityFramework%'
    OR c.content ILIKE '%TODO%'
    OR c.content ILIKE '%FIXME%'
    OR c.content ILIKE '%HACK%'
    """;

    private const string SemanticRuleCandidatePredicate = $"""
    (
        {RuleCandidatePredicate}
        OR c.file_path ILIKE '%handler%'
        OR c.file_path ILIKE '%service%'
        OR c.file_path ILIKE '%application%'
        OR c.file_path ILIKE '%domain%'
        OR c.file_path ILIKE '%usecase%'
        OR c.file_path ILIKE '%use_case%'
        OR c.file_path ILIKE '%policy%'
        OR c.file_path ILIKE '%policies%'
        OR c.file_path ILIKE '%specification%'
        OR c.file_path ILIKE '%specifications%'
        OR c.file_path ILIKE '%query%'
        OR c.symbol_name ILIKE '%handler%'
        OR c.symbol_name ILIKE '%service%'
        OR c.symbol_name ILIKE '%application%'
        OR c.symbol_name ILIKE '%domain%'
        OR c.symbol_name ILIKE '%usecase%'
        OR c.symbol_name ILIKE '%policy%'
        OR c.symbol_name ILIKE '%specification%'
        OR c.symbol_name ILIKE '%validar%'
        OR c.symbol_name ILIKE '%validate%'
        OR c.symbol_name ILIKE '%pode%'
        OR c.symbol_name ILIKE '%permite%'
        OR c.symbol_name ILIKE '%elegiv%'
        OR c.symbol_name ILIKE '%cancel%'
        OR c.symbol_name ILIKE '%aprova%'
        OR c.symbol_name ILIKE '%bloque%'
        OR c.symbol_name ILIKE '%venc%'
    )
    AND NOT (
        c.file_path ILIKE '%/constants/%'
        OR c.file_path ILIKE '%\\constants\\%'
        OR c.file_path ILIKE '%/constant/%'
        OR c.file_path ILIKE '%\\constant\\%'
        OR c.file_path ILIKE '%/mappings/%'
        OR c.file_path ILIKE '%\\mappings\\%'
        OR c.file_path ILIKE '%/mapping/%'
        OR c.file_path ILIKE '%\\mapping\\%'
        OR c.file_path ILIKE '%/entitiemappings/%'
        OR c.file_path ILIKE '%\\entitiemappings\\%'
        OR c.file_path ILIKE '%/configurations/%'
        OR c.file_path ILIKE '%\\configurations\\%'
        OR c.file_path ILIKE '%/configuration/%'
        OR c.file_path ILIKE '%\\configuration\\%'
        OR c.file_path ILIKE '%/options/%'
        OR c.file_path ILIKE '%\\options\\%'
        OR c.file_path ILIKE '%dto.cs'
        OR c.file_path ILIKE '%request.cs'
        OR c.file_path ILIKE '%response.cs'
        OR c.file_path ILIKE '%viewmodel.cs'
        OR c.content ILIKE '%IEntityTypeConfiguration<%'
        OR c.content ILIKE '%EntityTypeBuilder<%'
        OR (
            c.chunk_type = 'type'
            AND c.content ILIKE '% interface %'
        )
    )
    """;

    private readonly NpgsqlDataSource _dataSource;

    public PgVectorService(string connectionString)
    {
        _dataSource = CreateDataSource(connectionString);
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
        var entityFrameworkMigrationFilePredicate = EntityFrameworkMigrationFilePredicate("c");
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
        var testFilePredicate = TestFilePredicate("c", "p");
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
        SELECT p.name, r.title, r.description, r.source_file, r.symbol_name, r.status, r.evidence, r.confidence, r.embedding <=> $1 AS distance
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
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.GetDouble(8)));
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

    public async Task<ExtractionStats> GetRuleExtractionStatsAsync(string workspace, IReadOnlyList<string> projects, bool semantic, CancellationToken ct = default)
    {
        return await GetExtractionStatsAsync(workspace, projects, "rules", semantic ? SemanticRuleCandidatePredicate : RuleCandidatePredicate, ct);
    }

    public async Task<ExtractionStats> GetKnowledgeExtractionStatsAsync(string workspace, IReadOnlyList<string> projects, bool semantic, CancellationToken ct = default)
    {
        return await GetExtractionStatsAsync(workspace, projects, "knowledge", semantic ? "TRUE" : KnowledgeCandidatePredicate, ct);
    }

    public async Task<IReadOnlyList<ExtractionChunkResult>> GetChunksForRuleExtractionAsync(string workspace, IReadOnlyList<string> projects, int? limit, bool semantic, bool refresh, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var predicate = semantic ? SemanticRuleCandidatePredicate : RuleCandidatePredicate;
        var indexableFilePredicate = IndexableFilePredicate("c", "p");
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
        {LimitClause(limit)};
        """;
        cmd.Parameters.AddWithValue(workspace);
        cmd.Parameters.AddWithValue(projects.ToArray());
        return await ReadExtractionChunksAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<ExtractionChunkResult>> GetChunksForKnowledgeExtractionAsync(string workspace, IReadOnlyList<string> projects, int? limit, bool semantic, bool refresh, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var predicate = semantic ? "TRUE" : KnowledgeCandidatePredicate;
        var indexableFilePredicate = IndexableFilePredicate("c", "p");
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
        {LimitClause(limit)};
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

    public async Task MarkExtractionChunkProcessedAsync(Guid chunkId, string stage, string contentHash, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO ai_extraction_chunk_state(chunk_id, stage, content_hash, status, processed_at, error, updated_at)
        VALUES ($1, $2, $3, 'processed', NOW(), NULL, NOW())
        ON CONFLICT(chunk_id, stage)
        DO UPDATE SET content_hash = EXCLUDED.content_hash,
                      status = 'processed',
                      processed_at = NOW(),
                      error = NULL,
                      updated_at = NOW();
        """;
        cmd.Parameters.AddWithValue(chunkId);
        cmd.Parameters.AddWithValue(stage);
        cmd.Parameters.AddWithValue(contentHash);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkExtractionChunkFailedAsync(Guid chunkId, string stage, string contentHash, string error, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO ai_extraction_chunk_state(chunk_id, stage, content_hash, status, processed_at, error, updated_at)
        VALUES ($1, $2, $3, 'failed', NOW(), $4, NOW())
        ON CONFLICT(chunk_id, stage)
        DO UPDATE SET content_hash = EXCLUDED.content_hash,
                      status = 'failed',
                      processed_at = NOW(),
                      error = EXCLUDED.error,
                      updated_at = NOW();
        """;
        cmd.Parameters.AddWithValue(chunkId);
        cmd.Parameters.AddWithValue(stage);
        cmd.Parameters.AddWithValue(contentHash);
        cmd.Parameters.AddWithValue(error.Length <= 2_000 ? error : error[..2_000]);
        await cmd.ExecuteNonQueryAsync(ct);
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

    private async Task<ExtractionStats> GetExtractionStatsAsync(string workspace, IReadOnlyList<string> projects, string stage, string candidatePredicate, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var indexableFilePredicate = IndexableFilePredicate("c", "p");
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

    private static string LimitClause(int? limit)
    {
        return limit is null ? "" : $"LIMIT {limit.Value}";
    }

    private static string EntityFrameworkMigrationFilePredicate(string chunkAlias)
    {
        return $"""
        EXISTS (
            SELECT 1
            FROM ai_chunks ef_marker
            WHERE ef_marker.project_id = {chunkAlias}.project_id
              AND ef_marker.file_path = {chunkAlias}.file_path
              AND ({EntityFrameworkMigrationContentPredicate("ef_marker")})
        )
        """;
    }

    private static string NonEntityFrameworkMigrationFilePredicate(string chunkAlias)
    {
        return $"NOT ({EntityFrameworkMigrationFilePredicate(chunkAlias)})";
    }

    private static string IndexableFilePredicate(string chunkAlias, string projectAlias)
    {
        return $"""
        {NonEntityFrameworkMigrationFilePredicate(chunkAlias)}
        AND NOT ({TestFilePredicate(chunkAlias, projectAlias)})
        """;
    }

    private static string TestFilePredicate(string chunkAlias, string projectAlias)
    {
        return $"""
        (
            {projectAlias}.name ~* '(^|[._-])(test|tests|unittests|integrationtests|functionaltests|acceptancetests|spec|specs)([._-]|$)'
            OR {projectAlias}.name ~* '(tests|specs)$'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/test/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/unittests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/unit.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/integrationtests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/integration.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/functionaltests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/functional.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/acceptancetests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/acceptance.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/spec/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%/specs/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%.tests/%'
            OR replace({chunkAlias}.file_path, '\', '/') ILIKE '%.specs/%'
            OR replace({chunkAlias}.file_path, '\', '/') ~* '(tests|specs)\.cs$'
            OR EXISTS (
                SELECT 1
                FROM ai_chunks test_marker
                WHERE test_marker.project_id = {chunkAlias}.project_id
                  AND test_marker.file_path = {chunkAlias}.file_path
                  AND ({TestContentPredicate("test_marker")})
            )
        )
        """;
    }

    private static string EntityFrameworkMigrationContentPredicate(string alias)
    {
        return $"""
        {alias}.language = 'csharp'
        AND (
            {alias}.content ILIKE '%: Migration%'
            OR {alias}.content ILIKE '%:Migration%'
            OR {alias}.content ILIKE '%: ModelSnapshot%'
            OR {alias}.content ILIKE '%:ModelSnapshot%'
            OR {alias}.content ILIKE '%MigrationBuilder%'
            OR {alias}.content ILIKE '%BuildTargetModel%'
            OR {alias}.content ILIKE '%[Migration(%'
            OR (
                {alias}.content ILIKE '%[DbContext(%'
                AND {alias}.content ILIKE '%ProductVersion%'
            )
        )
        """;
    }

    private static string TestContentPredicate(string alias)
    {
        return $"""
        (
            {alias}.content ILIKE '%<IsTestProject>true</IsTestProject>%'
            OR {alias}.content ILIKE '%Microsoft.NET.Test.Sdk%'
            OR {alias}.content ILIKE '%MSTest.Sdk%'
            OR {alias}.content ILIKE '%MSTest.TestFramework%'
            OR {alias}.content ILIKE '%coverlet.collector%'
            OR {alias}.content ILIKE '%PackageReference Include="xunit"%'
            OR {alias}.content ILIKE '%PackageReference Include=''xunit''%'
            OR {alias}.content ILIKE '%PackageReference Include="NUnit"%'
            OR {alias}.content ILIKE '%PackageReference Include=''NUnit''%'
            OR (
                {alias}.language = 'csharp'
                AND (
                    {alias}.content ILIKE '%using Xunit%'
                    OR {alias}.content ILIKE '%using NUnit.Framework%'
                    OR {alias}.content ILIKE '%Microsoft.VisualStudio.TestTools.UnitTesting%'
                    OR {alias}.content ILIKE '%[Fact%'
                    OR {alias}.content ILIKE '%[Theory%'
                    OR {alias}.content ILIKE '%[Test%'
                    OR {alias}.content ILIKE '%[TestCase%'
                    OR {alias}.content ILIKE '%[TestMethod%'
                    OR {alias}.content ILIKE '%[TestClass%'
                    OR {alias}.content ILIKE '%[TestFixture%'
                    OR {alias}.content ILIKE '%[SetUp%'
                    OR {alias}.content ILIKE '%[OneTimeSetUp%'
                    OR {alias}.content ILIKE '%[TearDown%'
                    OR {alias}.content ILIKE '%[OneTimeTearDown%'
                )
            )
        )
        """;
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
    string? Symbol,
    string Status,
    string? Evidence,
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

public sealed record ExtractionChunkResult(
    Guid Id,
    string Project,
    string File,
    string? Language,
    string? ChunkType,
    string? Symbol,
    string Content,
    string ContentHash);

public sealed record ExtractionStats(
    long TotalChunks,
    long CandidateChunks,
    long ProcessedCandidateChunks,
    long PendingCandidateChunks,
    long FailedCandidateChunks,
    long ChangedCandidateChunks,
    long ActionableCandidateChunks);

public sealed record ExtractedBusinessRule(
    Guid ChunkId,
    string Title,
    string Description,
    string SourceFile,
    string? SymbolName,
    string Evidence,
    decimal Confidence,
    string ContentHash);

public sealed record ExtractedKnowledge(
    Guid ChunkId,
    string Kind,
    string Title,
    string Content,
    string Source,
    string? SymbolName,
    string Evidence,
    decimal Confidence,
    string ContentHash);

public sealed record UpsertExtractionResult(Guid? Id, string? Status, string Action);
