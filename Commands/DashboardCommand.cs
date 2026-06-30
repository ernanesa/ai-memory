using System.Net;
using System.Text;
using System.Text.Json;
using AiMemory.Configuration;
using Npgsql;

namespace AiMemory.Commands
{
  public static class DashboardCommand
  {
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = false
    };

    public static async Task RunAsync(string? workspace, string? project, string? db)
    {
      var config = await ConfigService.LoadAsync();
      await using var repository = new DashboardRepository(ConfigService.ResolveConnectionString(config, db));

      var overview = await repository.GetOverviewAsync(workspace, project);
      var workspaces = await repository.GetWorkspacesAsync(workspace, project);
      var projects = await repository.GetProjectsAsync(workspace, project, limit: 12);
      var health = await repository.GetHealthAsync(workspace, project);

      Console.WriteLine("AI Memory Dashboard");
      Console.WriteLine();
      Console.WriteLine($"Scope: {FormatScope(workspace, project)}");
      Console.WriteLine($"Last update: {FormatDate(overview.LastUpdated)}");
      Console.WriteLine();
      Console.WriteLine("Memory");
      Console.WriteLine($"  Workspaces:        {overview.WorkspaceCount:N0}");
      Console.WriteLine($"  Projects:          {overview.ProjectCount:N0}");
      Console.WriteLine($"  Files:             {overview.FileCount:N0}");
      Console.WriteLine($"  Chunks:            {overview.ChunkCount:N0}");
      Console.WriteLine($"  Business rules:    {overview.BusinessRuleCount:N0}");
      Console.WriteLine($"  Knowledge records: {overview.KnowledgeCount:N0}");
      Console.WriteLine();
      Console.WriteLine("Health");
      Console.WriteLine($"  Chunks without embedding: {health.ChunksWithoutEmbedding:N0}");
      Console.WriteLine($"  Projects without chunks:  {health.ProjectsWithoutChunks:N0}");
      Console.WriteLine($"  Duplicate project names:  {health.DuplicateProjectNames:N0}");
      Console.WriteLine($"  Broken chunk references:  {health.BrokenChunkReferences:N0}");
      Console.WriteLine($"  Candidate rules:          {health.CandidateBusinessRules:N0}");
      Console.WriteLine($"  Rules without evidence:   {health.BusinessRulesWithoutEvidence:N0}");
      Console.WriteLine($"  Rules without source:     {health.BusinessRulesWithoutSource:N0}");
      Console.WriteLine($"  Candidate knowledge:      {health.CandidateKnowledge:N0}");
      Console.WriteLine($"  Knowledge without evidence: {health.KnowledgeWithoutEvidence:N0}");
      Console.WriteLine($"  Knowledge without source: {health.KnowledgeWithoutSource:N0}");

      if (workspaces.Count > 0)
      {
        Console.WriteLine();
        Console.WriteLine("Workspaces");
        foreach (var item in workspaces)
        {
          Console.WriteLine($"  {item.Name,-24} {item.ProjectCount,4:N0} projects  {item.ChunkCount,8:N0} chunks  updated {FormatDate(item.LastUpdated)}");
        }
      }

      if (projects.Count > 0)
      {
        Console.WriteLine();
        Console.WriteLine("Projects");
        foreach (var item in projects)
        {
          Console.WriteLine($"  {item.Name,-32} {item.ChunkCount,8:N0} chunks  {item.FileCount,5:N0} files  {string.Join(", ", item.Workspaces)}");
        }
      }
    }

    public static async Task ServeAsync(string? workspace, string? project, string? db, int port)
    {
      var config = await ConfigService.LoadAsync();
      var connectionString = ConfigService.ResolveConnectionString(config, db);
      using var listener = new HttpListener();
      var prefix = $"http://localhost:{port}/";
      listener.Prefixes.Add(prefix);
      listener.Start();

      Console.WriteLine($"AI Memory dashboard running at {prefix}");
      Console.WriteLine("Press Ctrl+C to stop.");

      var stopping = new TaskCompletionSource();
      Console.CancelKeyPress += (_, args) =>
      {
        args.Cancel = true;
        stopping.TrySetResult();
        listener.Stop();
      };

      while (!stopping.Task.IsCompleted)
      {
        HttpListenerContext context;
        try
        {
          context = await listener.GetContextAsync();
        }
        catch (HttpListenerException) when (stopping.Task.IsCompleted || !listener.IsListening)
        {
          break;
        }
        catch (ObjectDisposedException)
        {
          break;
        }

        _ = Task.Run(async () =>
        {
          try
          {
            await HandleRequestAsync(context, connectionString, workspace, project);
          }
          catch (Exception ex)
          {
            await WriteJsonAsync(context.Response, new { error = ex.Message }, HttpStatusCode.InternalServerError);
          }
        });
      }
    }

    private static async Task HandleRequestAsync(HttpListenerContext context, string connectionString, string? defaultWorkspace, string? defaultProject)
    {
      var request = context.Request;
      var path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
      if (string.IsNullOrEmpty(path))
      {
        path = "/";
      }

      if (path == "/")
      {
        await WriteHtmlAsync(context.Response, Html);
        return;
      }

      if (!path.StartsWith("/api/", StringComparison.Ordinal))
      {
        await WriteTextAsync(context.Response, "Not found", "text/plain; charset=utf-8", HttpStatusCode.NotFound);
        return;
      }

      var query = ParseQuery(request.Url?.Query);
      var workspace = FirstNonEmpty(GetQuery(query, "workspace"), defaultWorkspace);
      var project = FirstNonEmpty(GetQuery(query, "project"), defaultProject);
      await using var repository = new DashboardRepository(connectionString);

      object payload = path switch
      {
        "/api/overview" => await repository.GetOverviewAsync(workspace, project),
        "/api/workspaces" => await repository.GetWorkspacesAsync(workspace, project),
        "/api/projects" => await repository.GetProjectsAsync(workspace, project, GetLimit(query, 100, 1, 500)),
        "/api/chunks" => await repository.GetChunksAsync(workspace, project, GetQuery(query, "q"), GetLimit(query, 100, 1, 500)),
        "/api/business-rules" => await repository.GetBusinessRulesAsync(workspace, project, GetQuery(query, "q"), GetLimit(query, 100, 1, 500)),
        "/api/knowledge" => await repository.GetKnowledgeAsync(workspace, project, GetQuery(query, "q"), GetLimit(query, 100, 1, 500)),
        "/api/health" => await repository.GetHealthAsync(workspace, project),
        _ => new { error = "Unknown endpoint." }
      };

      await WriteJsonAsync(context.Response, payload);
    }

    private static string FormatScope(string? workspace, string? project)
    {
      return (Normalize(workspace), Normalize(project)) switch
      {
        (null, null) => "all workspaces and projects",
        (string w, null) => $"workspace {w}",
        (null, string p) => $"project {p}",
        (string w, string p) => $"workspace {w}, project {p}"
      };
    }

    private static string FormatDate(DateTime? value)
    {
      return value is null ? "never" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, string html)
    {
      await WriteTextAsync(response, html, "text/html; charset=utf-8", HttpStatusCode.OK);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
      var json = JsonSerializer.Serialize(payload, JsonOptions);
      await WriteTextAsync(response, json, "application/json; charset=utf-8", statusCode);
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, string text, string contentType, HttpStatusCode statusCode)
    {
      var bytes = Encoding.UTF8.GetBytes(text);
      response.StatusCode = (int)statusCode;
      response.ContentType = contentType;
      response.ContentLength64 = bytes.Length;
      await response.OutputStream.WriteAsync(bytes);
      response.Close();
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
      var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      if (string.IsNullOrWhiteSpace(query))
      {
        return result;
      }

      foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
      {
        var pieces = part.Split('=', 2);
        var key = Uri.UnescapeDataString(pieces[0].Replace('+', ' '));
        var value = pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1].Replace('+', ' ')) : "";
        result[key] = value;
      }

      return result;
    }

    private static int GetLimit(Dictionary<string, string> query, int defaultValue, int min, int max)
    {
      if (!query.TryGetValue("limit", out var raw) || !int.TryParse(raw, out var value))
      {
        return defaultValue;
      }

      return Math.Clamp(value, min, max);
    }

    private static string? GetQuery(Dictionary<string, string> query, string key)
    {
      return query.TryGetValue(key, out var value) ? Normalize(value) : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
      return values.Select(Normalize).FirstOrDefault(value => value is not null);
    }

    private static string? Normalize(string? value)
    {
      return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class DashboardRepository(string connectionString) : IAsyncDisposable
    {
      private readonly NpgsqlConnection _connection = new(connectionString);

      public async ValueTask DisposeAsync()
      {
        await _connection.DisposeAsync();
      }

      public async Task<DashboardOverview> GetOverviewAsync(string? workspace, string? project)
      {
        await EnsureOpenAsync();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            WITH filtered_projects AS (
                SELECT DISTINCT p.id
                FROM ai_projects p
                LEFT JOIN ai_workspace_projects wp ON wp.project_id = p.id
                LEFT JOIN ai_workspaces w ON w.id = wp.workspace_id
                WHERE ($1::text IS NULL OR w.name = $1)
                  AND ($2::text IS NULL OR p.name = $2 OR p.root_path ILIKE '%' || $2 || '%' OR w.name || '/' || p.name = $2)
            )
            SELECT
                (SELECT count(DISTINCT w.id)
                 FROM ai_workspaces w
                 LEFT JOIN ai_workspace_projects wp ON wp.workspace_id = w.id
                 LEFT JOIN filtered_projects fp ON fp.id = wp.project_id
                 WHERE ($1::text IS NULL OR w.name = $1)
                   AND ($2::text IS NULL OR fp.id IS NOT NULL)) AS workspaces,
                (SELECT count(*) FROM filtered_projects) AS projects,
                (SELECT count(*) FROM ai_chunks c JOIN filtered_projects fp ON fp.id = c.project_id) AS chunks,
                (SELECT count(DISTINCT c.project_id::text || ':' || c.file_path) FROM ai_chunks c JOIN filtered_projects fp ON fp.id = c.project_id) AS files,
                (SELECT count(*) FROM ai_business_rules r JOIN filtered_projects fp ON fp.id = r.project_id) AS business_rules,
                (SELECT count(*) FROM ai_knowledge k JOIN filtered_projects fp ON fp.id = k.project_id) AS knowledge,
                (SELECT max(c.updated_at) FROM ai_chunks c JOIN filtered_projects fp ON fp.id = c.project_id) AS last_updated;
            """;
        AddScopeParameters(cmd, workspace, project);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new DashboardOverview(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            GetNullableDateTime(reader, 6));
      }

      public async Task<IReadOnlyList<WorkspaceSummary>> GetWorkspacesAsync(string? workspace, string? project)
      {
        await EnsureOpenAsync();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            WITH filtered_projects AS (
                SELECT DISTINCT p.id, p.name, p.root_path
                FROM ai_projects p
                LEFT JOIN ai_workspace_projects wp ON wp.project_id = p.id
                LEFT JOIN ai_workspaces w ON w.id = wp.workspace_id
                WHERE ($1::text IS NULL OR w.name = $1)
                  AND ($2::text IS NULL OR p.name = $2 OR p.root_path ILIKE '%' || $2 || '%' OR w.name || '/' || p.name = $2)
            )
            SELECT w.name,
                   count(DISTINCT fp.id) AS projects,
                   count(DISTINCT c.id) AS chunks,
                   count(DISTINCT r.id) AS business_rules,
                   count(DISTINCT k.id) AS knowledge,
                   max(c.updated_at) AS last_updated
            FROM ai_workspaces w
            LEFT JOIN ai_workspace_projects wp ON wp.workspace_id = w.id
            LEFT JOIN filtered_projects fp ON fp.id = wp.project_id
            LEFT JOIN ai_chunks c ON c.project_id = fp.id
            LEFT JOIN ai_business_rules r ON r.project_id = fp.id
            LEFT JOIN ai_knowledge k ON k.project_id = fp.id
            WHERE ($1::text IS NULL OR w.name = $1)
              AND ($2::text IS NULL OR fp.id IS NOT NULL)
            GROUP BY w.name
            ORDER BY w.name;
            """;
        AddScopeParameters(cmd, workspace, project);

        var rows = new List<WorkspaceSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
          rows.Add(new WorkspaceSummary(
              reader.GetString(0),
              reader.GetInt64(1),
              reader.GetInt64(2),
              reader.GetInt64(3),
              reader.GetInt64(4),
              GetNullableDateTime(reader, 5)));
        }

        return rows;
      }

      public async Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(string? workspace, string? project, int limit)
      {
        await EnsureOpenAsync();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            WITH filtered_projects AS (
                SELECT DISTINCT p.id, p.name, p.root_path
                FROM ai_projects p
                LEFT JOIN ai_workspace_projects wp ON wp.project_id = p.id
                LEFT JOIN ai_workspaces w ON w.id = wp.workspace_id
                WHERE ($1::text IS NULL OR w.name = $1)
                  AND ($2::text IS NULL OR p.name = $2 OR p.root_path ILIKE '%' || $2 || '%' OR w.name || '/' || p.name = $2)
            )
            SELECT fp.name,
                   fp.root_path,
                   COALESCE(string_agg(DISTINCT w.name, ' | ' ORDER BY w.name) FILTER (WHERE w.name IS NOT NULL), '') AS workspaces,
                   count(DISTINCT c.id) AS chunks,
                   count(DISTINCT c.file_path) AS files,
                   count(DISTINCT r.id) AS business_rules,
                   count(DISTINCT k.id) AS knowledge,
                   max(c.updated_at) AS last_updated,
                   COALESCE(string_agg(DISTINCT c.language, ', ' ORDER BY c.language) FILTER (WHERE c.language IS NOT NULL), '') AS languages
            FROM filtered_projects fp
            LEFT JOIN ai_workspace_projects wp ON wp.project_id = fp.id
            LEFT JOIN ai_workspaces w ON w.id = wp.workspace_id
            LEFT JOIN ai_chunks c ON c.project_id = fp.id
            LEFT JOIN ai_business_rules r ON r.project_id = fp.id
            LEFT JOIN ai_knowledge k ON k.project_id = fp.id
            GROUP BY fp.id, fp.name, fp.root_path
            ORDER BY count(DISTINCT c.id) DESC, fp.name
            LIMIT $3;
            """;
        AddScopeParameters(cmd, workspace, project);
        cmd.Parameters.AddWithValue(limit);

        var rows = new List<ProjectSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
          rows.Add(new ProjectSummary(
              reader.GetString(0),
              reader.GetString(1),
              SplitList(reader.GetString(2), " | "),
              reader.GetInt64(3),
              reader.GetInt64(4),
              reader.GetInt64(5),
              reader.GetInt64(6),
              GetNullableDateTime(reader, 7),
              SplitList(reader.GetString(8), ", ")));
        }

        return rows;
      }

      public async Task<IReadOnlyList<ChunkSummary>> GetChunksAsync(string? workspace, string? project, string? search, int limit)
      {
        await EnsureOpenAsync();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            WITH filtered_projects AS (
                SELECT DISTINCT p.id, p.name
                FROM ai_projects p
                LEFT JOIN ai_workspace_projects wp ON wp.project_id = p.id
                LEFT JOIN ai_workspaces w ON w.id = wp.workspace_id
                WHERE ($1::text IS NULL OR w.name = $1)
                  AND ($2::text IS NULL OR p.name = $2 OR p.root_path ILIKE '%' || $2 || '%' OR w.name || '/' || p.name = $2)
            )
            SELECT p.name,
                   COALESCE(string_agg(DISTINCT w.name, ' | ' ORDER BY w.name) FILTER (WHERE w.name IS NOT NULL), '') AS workspaces,
                   c.file_path,
                   c.language,
                   c.chunk_type,
                   c.symbol_name,
                   length(c.content)::bigint AS content_length,
                   c.embedding IS NOT NULL AS has_embedding,
                   c.updated_at
            FROM ai_chunks c
            JOIN filtered_projects p ON p.id = c.project_id
            LEFT JOIN ai_workspace_projects wp ON wp.project_id = p.id
            LEFT JOIN ai_workspaces w ON w.id = wp.workspace_id
            WHERE ($3::text IS NULL
                   OR c.file_path ILIKE '%' || $3 || '%'
                   OR c.language ILIKE '%' || $3 || '%'
                   OR c.chunk_type ILIKE '%' || $3 || '%'
                   OR c.symbol_name ILIKE '%' || $3 || '%')
            GROUP BY c.id, p.name, c.file_path, c.language, c.chunk_type, c.symbol_name, c.content, c.embedding, c.updated_at
            ORDER BY c.updated_at DESC
            LIMIT $4;
            """;
        AddScopeParameters(cmd, workspace, project);
        cmd.Parameters.AddWithValue((object?)Normalize(search) ?? DBNull.Value);
        cmd.Parameters.AddWithValue(limit);

        var rows = new List<ChunkSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
          rows.Add(new ChunkSummary(
              reader.GetString(0),
              SplitList(reader.GetString(1), " | "),
              reader.GetString(2),
              GetNullableString(reader, 3),
              GetNullableString(reader, 4),
              GetNullableString(reader, 5),
              reader.GetInt64(6),
              reader.GetBoolean(7),
              GetNullableDateTime(reader, 8)));
        }

        return rows;
      }

      public async Task<IReadOnlyList<BusinessRuleSummary>> GetBusinessRulesAsync(string? workspace, string? project, string? search, int limit)
      {
        await EnsureOpenAsync();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            WITH filtered_projects AS (
                SELECT DISTINCT p.id, p.name
                FROM ai_projects p
                LEFT JOIN ai_workspace_projects wp ON wp.project_id = p.id
                LEFT JOIN ai_workspaces w ON w.id = wp.workspace_id
                WHERE ($1::text IS NULL OR w.name = $1)
                  AND ($2::text IS NULL OR p.name = $2 OR p.root_path ILIKE '%' || $2 || '%' OR w.name || '/' || p.name = $2)
            )
            SELECT p.name, r.title, r.description, r.source_file, r.symbol_name, r.status, r.evidence, r.confidence, r.created_at, r.updated_at
            FROM ai_business_rules r
            LEFT JOIN filtered_projects p ON p.id = r.project_id
            WHERE p.id IS NOT NULL
              AND ($3::text IS NULL
                   OR r.title ILIKE '%' || $3 || '%'
                   OR r.description ILIKE '%' || $3 || '%'
                   OR r.source_file ILIKE '%' || $3 || '%'
                   OR r.symbol_name ILIKE '%' || $3 || '%'
                   OR r.status ILIKE '%' || $3 || '%'
                   OR r.evidence ILIKE '%' || $3 || '%')
            ORDER BY r.updated_at DESC
            LIMIT $4;
            """;
        AddScopeParameters(cmd, workspace, project);
        cmd.Parameters.AddWithValue((object?)Normalize(search) ?? DBNull.Value);
        cmd.Parameters.AddWithValue(limit);

        var rows = new List<BusinessRuleSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
          rows.Add(new BusinessRuleSummary(
              reader.GetString(0),
              reader.GetString(1),
              reader.GetString(2),
              GetNullableString(reader, 3),
              GetNullableString(reader, 4),
              reader.GetString(5),
              GetNullableString(reader, 6),
              reader.IsDBNull(7) ? null : reader.GetDecimal(7),
              GetNullableDateTime(reader, 8),
              GetNullableDateTime(reader, 9)));
        }

        return rows;
      }

      public async Task<IReadOnlyList<KnowledgeSummary>> GetKnowledgeAsync(string? workspace, string? project, string? search, int limit)
      {
        await EnsureOpenAsync();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            WITH filtered_projects AS (
                SELECT DISTINCT p.id, p.name
                FROM ai_projects p
                LEFT JOIN ai_workspace_projects wp ON wp.project_id = p.id
                LEFT JOIN ai_workspaces w ON w.id = wp.workspace_id
                WHERE ($1::text IS NULL OR w.name = $1)
                  AND ($2::text IS NULL OR p.name = $2 OR p.root_path ILIKE '%' || $2 || '%' OR w.name || '/' || p.name = $2)
            )
            SELECT p.name, k.kind, k.title, k.content, k.source, k.symbol_name, k.status, k.evidence, k.confidence, k.updated_at
            FROM ai_knowledge k
            LEFT JOIN filtered_projects p ON p.id = k.project_id
            WHERE p.id IS NOT NULL
              AND ($3::text IS NULL
                   OR k.kind ILIKE '%' || $3 || '%'
                   OR k.title ILIKE '%' || $3 || '%'
                   OR k.content ILIKE '%' || $3 || '%'
                   OR k.source ILIKE '%' || $3 || '%'
                   OR k.symbol_name ILIKE '%' || $3 || '%'
                   OR k.status ILIKE '%' || $3 || '%'
                   OR k.evidence ILIKE '%' || $3 || '%')
            ORDER BY k.updated_at DESC
            LIMIT $4;
            """;
        AddScopeParameters(cmd, workspace, project);
        cmd.Parameters.AddWithValue((object?)Normalize(search) ?? DBNull.Value);
        cmd.Parameters.AddWithValue(limit);

        var rows = new List<KnowledgeSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
          rows.Add(new KnowledgeSummary(
              reader.GetString(0),
              reader.GetString(1),
              reader.GetString(2),
              reader.GetString(3),
              GetNullableString(reader, 4),
              GetNullableString(reader, 5),
              reader.GetString(6),
              GetNullableString(reader, 7),
              reader.IsDBNull(8) ? null : reader.GetDecimal(8),
              GetNullableDateTime(reader, 9)));
        }

        return rows;
      }

      public async Task<HealthSummary> GetHealthAsync(string? workspace, string? project)
      {
        await EnsureOpenAsync();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            WITH filtered_projects AS (
                SELECT DISTINCT p.id, p.name
                FROM ai_projects p
                LEFT JOIN ai_workspace_projects wp ON wp.project_id = p.id
                LEFT JOIN ai_workspaces w ON w.id = wp.workspace_id
                WHERE ($1::text IS NULL OR w.name = $1)
                  AND ($2::text IS NULL OR p.name = $2 OR w.name || '/' || p.name = $2)
            )
            SELECT
                (SELECT count(*) FROM ai_chunks c JOIN filtered_projects fp ON fp.id = c.project_id WHERE c.embedding IS NULL) AS chunks_without_embedding,
                (SELECT count(*) FROM filtered_projects fp WHERE NOT EXISTS (SELECT 1 FROM ai_chunks c WHERE c.project_id = fp.id)) AS projects_without_chunks,
                (SELECT count(*) FROM (SELECT name FROM filtered_projects GROUP BY name HAVING count(*) > 1) duplicates) AS duplicate_project_names,
                (SELECT count(*) FROM ai_chunks c LEFT JOIN ai_projects p ON p.id = c.project_id WHERE p.id IS NULL) AS broken_chunk_references,
                (SELECT count(*) FROM ai_business_rules r JOIN filtered_projects fp ON fp.id = r.project_id WHERE r.status = 'candidate') AS candidate_business_rules,
                (SELECT count(*) FROM ai_business_rules r JOIN filtered_projects fp ON fp.id = r.project_id WHERE r.status <> 'rejected' AND NULLIF(btrim(COALESCE(r.evidence, '')), '') IS NULL) AS business_rules_without_evidence,
                (SELECT count(*) FROM ai_business_rules r JOIN filtered_projects fp ON fp.id = r.project_id WHERE r.status <> 'rejected' AND NULLIF(btrim(COALESCE(r.source_file, '')), '') IS NULL) AS business_rules_without_source,
                (SELECT count(*) FROM ai_knowledge k JOIN filtered_projects fp ON fp.id = k.project_id WHERE k.status = 'candidate') AS candidate_knowledge,
                (SELECT count(*) FROM ai_knowledge k JOIN filtered_projects fp ON fp.id = k.project_id WHERE k.status <> 'rejected' AND NULLIF(btrim(COALESCE(k.evidence, '')), '') IS NULL) AS knowledge_without_evidence,
                (SELECT count(*) FROM ai_knowledge k JOIN filtered_projects fp ON fp.id = k.project_id WHERE k.status <> 'rejected' AND NULLIF(btrim(COALESCE(k.source, '')), '') IS NULL) AS knowledge_without_source;
            """;
        AddScopeParameters(cmd, workspace, project);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new HealthSummary(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9));
      }

      private async Task EnsureOpenAsync()
      {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
          await _connection.OpenAsync();
        }
      }

      private static void AddScopeParameters(NpgsqlCommand cmd, string? workspace, string? project)
      {
        cmd.Parameters.AddWithValue((object?)Normalize(workspace) ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)Normalize(project) ?? DBNull.Value);
      }

      private static string? Normalize(string? value)
      {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
      }

      private static string? GetNullableString(NpgsqlDataReader reader, int ordinal)
      {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
      }

      private static DateTime? GetNullableDateTime(NpgsqlDataReader reader, int ordinal)
      {
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
      }

      private static IReadOnlyList<string> SplitList(string value, string separator)
      {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      }
    }

    public sealed record DashboardOverview(
        long WorkspaceCount,
        long ProjectCount,
        long ChunkCount,
        long FileCount,
        long BusinessRuleCount,
        long KnowledgeCount,
        DateTime? LastUpdated);

    public sealed record WorkspaceSummary(
        string Name,
        long ProjectCount,
        long ChunkCount,
        long BusinessRuleCount,
        long KnowledgeCount,
        DateTime? LastUpdated);

    public sealed record ProjectSummary(
        string Name,
        string RootPath,
        IReadOnlyList<string> Workspaces,
        long ChunkCount,
        long FileCount,
        long BusinessRuleCount,
        long KnowledgeCount,
        DateTime? LastUpdated,
        IReadOnlyList<string> Languages);

    public sealed record ChunkSummary(
        string Project,
        IReadOnlyList<string> Workspaces,
        string File,
        string? Language,
        string? ChunkType,
        string? Symbol,
        long ContentLength,
        bool HasEmbedding,
        DateTime? UpdatedAt);

    public sealed record BusinessRuleSummary(
        string Project,
        string Title,
        string Description,
        string? SourceFile,
        string? Symbol,
        string Status,
        string? Evidence,
        decimal? Confidence,
        DateTime? CreatedAt,
        DateTime? UpdatedAt);

    public sealed record KnowledgeSummary(
        string Project,
        string Kind,
        string Title,
        string Content,
        string? Source,
        string? Symbol,
        string Status,
        string? Evidence,
        decimal? Confidence,
        DateTime? UpdatedAt);

    public sealed record HealthSummary(
        long ChunksWithoutEmbedding,
        long ProjectsWithoutChunks,
        long DuplicateProjectNames,
        long BrokenChunkReferences,
        long CandidateBusinessRules,
        long BusinessRulesWithoutEvidence,
        long BusinessRulesWithoutSource,
        long CandidateKnowledge,
        long KnowledgeWithoutEvidence,
        long KnowledgeWithoutSource);

    private const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>AI Memory Dashboard</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #f7f8fa;
      --panel: #ffffff;
      --panel-soft: #f1f5f9;
      --text: #172033;
      --muted: #667085;
      --line: #d8dee8;
      --accent: #0f766e;
      --accent-strong: #0b5f59;
      --warning: #b45309;
      --danger: #b42318;
      --radius: 8px;
      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
    }
    * { box-sizing: border-box; }
    body { margin: 0; background: var(--bg); color: var(--text); }
    header {
      display: flex; align-items: center; justify-content: space-between; gap: 16px;
      padding: 18px 24px; border-bottom: 1px solid var(--line); background: var(--panel);
      position: sticky; top: 0; z-index: 3;
    }
    h1 { margin: 0; font-size: 20px; font-weight: 650; }
    main { padding: 20px 24px 28px; max-width: 1480px; margin: 0 auto; }
    .filters { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
    input, select, button {
      height: 36px; border: 1px solid var(--line); border-radius: 6px; background: #fff; color: var(--text);
      padding: 0 10px; font: inherit; min-width: 150px;
    }
    button { min-width: auto; cursor: pointer; background: var(--accent); color: #fff; border-color: var(--accent); font-weight: 600; }
    button:hover { background: var(--accent-strong); }
    .tabs { display: flex; gap: 4px; margin: 0 0 16px; border-bottom: 1px solid var(--line); overflow-x: auto; }
    .tab {
      border: 0; border-bottom: 2px solid transparent; border-radius: 0; background: transparent; color: var(--muted);
      padding: 10px 12px; height: 42px;
    }
    .tab.active { color: var(--accent-strong); border-bottom-color: var(--accent); }
    .metrics { display: grid; grid-template-columns: repeat(6, minmax(130px, 1fr)); gap: 12px; margin-bottom: 18px; }
    .metric, .panel {
      background: var(--panel); border: 1px solid var(--line); border-radius: var(--radius);
    }
    .metric { padding: 14px; min-height: 84px; }
    .metric .label { color: var(--muted); font-size: 12px; line-height: 1.3; }
    .metric .value { margin-top: 8px; font-size: 26px; font-weight: 700; letter-spacing: 0; }
    .grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(320px, 0.42fr); gap: 14px; }
    .panel { overflow: hidden; }
    .panel h2 {
      margin: 0; padding: 13px 14px; font-size: 15px; border-bottom: 1px solid var(--line); background: var(--panel-soft);
    }
    .panel-body { padding: 14px; }
    table { width: 100%; border-collapse: collapse; table-layout: fixed; }
    th, td { padding: 9px 10px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: top; font-size: 13px; }
    th { color: var(--muted); font-weight: 650; background: #fbfcfe; }
    tr:last-child td { border-bottom: 0; }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 12px; }
    .muted { color: var(--muted); }
    .truncate { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .section { display: none; }
    .section.active { display: block; }
    .toolbar { display: flex; gap: 10px; margin-bottom: 12px; flex-wrap: wrap; }
    .toolbar input { min-width: min(360px, 100%); }
    .status-ok { color: var(--accent-strong); font-weight: 650; }
    .status-warn { color: var(--warning); font-weight: 650; }
    .status-danger { color: var(--danger); font-weight: 650; }
    @media (max-width: 980px) {
      header { align-items: flex-start; flex-direction: column; }
      main { padding: 14px; }
      .metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .grid { grid-template-columns: 1fr; }
      table { min-width: 780px; }
      .panel { overflow-x: auto; }
    }
  </style>
</head>
<body>
  <header>
    <h1>AI Memory Dashboard</h1>
    <div class="filters">
      <select id="workspace"></select>
      <input id="project" placeholder="Project filter">
      <button id="refresh">Refresh</button>
    </div>
  </header>
  <main>
    <nav class="tabs">
      <button class="tab active" data-tab="overview">Overview</button>
      <button class="tab" data-tab="projects">Projects</button>
      <button class="tab" data-tab="chunks">Chunks</button>
      <button class="tab" data-tab="rules">Business Rules</button>
      <button class="tab" data-tab="knowledge">Knowledge</button>
      <button class="tab" data-tab="health">Health</button>
    </nav>

    <section id="overview" class="section active">
      <div class="metrics" id="metrics"></div>
      <div class="grid">
        <div class="panel"><h2>Projects</h2><div class="panel-body" id="overview-projects"></div></div>
        <div class="panel"><h2>Workspaces</h2><div class="panel-body" id="overview-workspaces"></div></div>
      </div>
    </section>

    <section id="projects" class="section">
      <div class="panel"><h2>Projects</h2><div class="panel-body" id="projects-table"></div></div>
    </section>

    <section id="chunks" class="section">
      <div class="toolbar"><input id="chunk-search" placeholder="Filter by file, language, type or symbol"><button data-action="chunks">Search</button></div>
      <div class="panel"><h2>Chunks</h2><div class="panel-body" id="chunks-table"></div></div>
    </section>

    <section id="rules" class="section">
      <div class="toolbar"><input id="rules-search" placeholder="Filter rules"><button data-action="rules">Search</button></div>
      <div class="panel"><h2>Business Rules</h2><div class="panel-body" id="rules-table"></div></div>
    </section>

    <section id="knowledge" class="section">
      <div class="toolbar"><input id="knowledge-search" placeholder="Filter knowledge"><button data-action="knowledge">Search</button></div>
      <div class="panel"><h2>Knowledge</h2><div class="panel-body" id="knowledge-table"></div></div>
    </section>

    <section id="health" class="section">
      <div class="panel"><h2>Health</h2><div class="panel-body" id="health-panel"></div></div>
    </section>
  </main>
  <script>
    const state = { tab: "overview" };
    const number = value => Number(value || 0).toLocaleString();
    const date = value => value ? new Date(value).toLocaleString() : "never";
    const text = value => value == null || value === "" ? "-" : String(value);
    const esc = value => text(value).replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[ch]));

    function params(extra = {}) {
      const query = new URLSearchParams();
      const workspace = document.getElementById("workspace").value;
      const project = document.getElementById("project").value.trim();
      if (workspace) query.set("workspace", workspace);
      if (project) query.set("project", project);
      for (const [key, value] of Object.entries(extra)) if (value) query.set(key, value);
      return query.toString();
    }

    async function get(path, extra) {
      const query = params(extra);
      const response = await fetch(query ? `${path}?${query}` : path);
      if (!response.ok) throw new Error(await response.text());
      return response.json();
    }

    function table(columns, rows) {
      if (!rows.length) return `<p class="muted">No records found.</p>`;
      const head = columns.map(col => `<th style="width:${col.width || "auto"}">${esc(col.label)}</th>`).join("");
      const body = rows.map(row => `<tr>${columns.map(col => `<td class="${col.class || ""}">${col.render ? col.render(row) : esc(row[col.key])}</td>`).join("")}</tr>`).join("");
      return `<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
    }

    async function loadWorkspaces() {
      const current = document.getElementById("workspace").value;
      const data = await fetch("/api/workspaces").then(r => r.json());
      document.getElementById("workspace").innerHTML = `<option value="">All workspaces</option>` + data.map(item => `<option value="${esc(item.name)}">${esc(item.name)}</option>`).join("");
      document.getElementById("workspace").value = current;
    }

    async function loadOverview() {
      const [overview, workspaces, projects] = await Promise.all([
        get("/api/overview"),
        get("/api/workspaces"),
        get("/api/projects", { limit: 12 })
      ]);
      const metrics = [
        ["Workspaces", overview.workspaceCount],
        ["Projects", overview.projectCount],
        ["Files", overview.fileCount],
        ["Chunks", overview.chunkCount],
        ["Business rules", overview.businessRuleCount],
        ["Knowledge", overview.knowledgeCount]
      ];
      document.getElementById("metrics").innerHTML = metrics.map(([label, value]) => `<div class="metric"><div class="label">${label}</div><div class="value">${number(value)}</div></div>`).join("");
      document.getElementById("overview-workspaces").innerHTML = table([
        { label: "Workspace", key: "name" },
        { label: "Projects", render: r => number(r.projectCount), width: "95px" },
        { label: "Chunks", render: r => number(r.chunkCount), width: "100px" },
        { label: "Updated", render: r => date(r.lastUpdated), width: "170px" }
      ], workspaces);
      document.getElementById("overview-projects").innerHTML = renderProjects(projects);
    }

    function renderProjects(projects) {
      return table([
        { label: "Project", key: "name", width: "22%" },
        { label: "Workspaces", render: r => esc((r.workspaces || []).join(", ")) },
        { label: "Languages", render: r => esc((r.languages || []).join(", ")) },
        { label: "Files", render: r => number(r.fileCount), width: "90px" },
        { label: "Chunks", render: r => number(r.chunkCount), width: "100px" },
        { label: "Updated", render: r => date(r.lastUpdated), width: "170px" }
      ], projects);
    }

    async function loadProjects() {
      document.getElementById("projects-table").innerHTML = renderProjects(await get("/api/projects", { limit: 500 }));
    }

    async function loadChunks() {
      const q = document.getElementById("chunk-search").value.trim();
      const rows = await get("/api/chunks", { q, limit: 200 });
      document.getElementById("chunks-table").innerHTML = table([
        { label: "Project", key: "project", width: "18%" },
        { label: "File", key: "file", class: "mono truncate" },
        { label: "Language", key: "language", width: "100px" },
        { label: "Type", key: "chunkType", width: "95px" },
        { label: "Symbol", key: "symbol", width: "160px" },
        { label: "Chars", render: r => number(r.contentLength), width: "85px" },
        { label: "Embedding", render: r => r.hasEmbedding ? `<span class="status-ok">ok</span>` : `<span class="status-danger">missing</span>`, width: "105px" },
        { label: "Updated", render: r => date(r.updatedAt), width: "170px" }
      ], rows);
    }

    async function loadRules() {
      const q = document.getElementById("rules-search").value.trim();
      const rows = await get("/api/business-rules", { q, limit: 200 });
      document.getElementById("rules-table").innerHTML = table([
        { label: "Project", key: "project", width: "18%" },
        { label: "Status", render: r => `<span class="${r.status === "accepted" ? "status-ok" : r.status === "rejected" ? "status-danger" : "status-warn"}">${esc(r.status)}</span>`, width: "105px" },
        { label: "Title", key: "title", width: "18%" },
        { label: "Description", key: "description" },
        { label: "Evidence", key: "evidence", width: "22%" },
        { label: "Source", render: r => esc([r.sourceFile, r.symbol].filter(Boolean).join(" :: ") || "-"), class: "mono truncate", width: "22%" },
        { label: "Confidence", render: r => text(r.confidence), width: "100px" }
      ], rows);
    }

    async function loadKnowledge() {
      const q = document.getElementById("knowledge-search").value.trim();
      const rows = await get("/api/knowledge", { q, limit: 200 });
      document.getElementById("knowledge-table").innerHTML = table([
        { label: "Project", key: "project", width: "16%" },
        { label: "Status", render: r => `<span class="${r.status === "accepted" ? "status-ok" : r.status === "rejected" ? "status-danger" : "status-warn"}">${esc(r.status)}</span>`, width: "105px" },
        { label: "Kind", key: "kind", width: "150px" },
        { label: "Title", key: "title", width: "18%" },
        { label: "Content", key: "content" },
        { label: "Evidence", key: "evidence", width: "20%" },
        { label: "Source", render: r => esc([r.source, r.symbol].filter(Boolean).join(" :: ") || "-"), class: "mono truncate", width: "20%" },
        { label: "Confidence", render: r => text(r.confidence), width: "100px" }
      ], rows);
    }

    async function loadHealth() {
      const health = await get("/api/health");
      const rows = [
        ["Chunks without embedding", health.chunksWithoutEmbedding],
        ["Projects without chunks", health.projectsWithoutChunks],
        ["Duplicate project names", health.duplicateProjectNames],
        ["Broken chunk references", health.brokenChunkReferences],
        ["Candidate business rules", health.candidateBusinessRules],
        ["Business rules without evidence", health.businessRulesWithoutEvidence],
        ["Business rules without source", health.businessRulesWithoutSource],
        ["Candidate knowledge", health.candidateKnowledge],
        ["Knowledge without evidence", health.knowledgeWithoutEvidence],
        ["Knowledge without source", health.knowledgeWithoutSource]
      ].map(([name, value]) => ({ name, value }));
      document.getElementById("health-panel").innerHTML = table([
        { label: "Check", key: "name" },
        { label: "Value", render: r => `<span class="${r.value ? "status-warn" : "status-ok"}">${number(r.value)}</span>`, width: "120px" }
      ], rows);
    }

    async function refresh() {
      try {
        if (state.tab === "overview") await loadOverview();
        if (state.tab === "projects") await loadProjects();
        if (state.tab === "chunks") await loadChunks();
        if (state.tab === "rules") await loadRules();
        if (state.tab === "knowledge") await loadKnowledge();
        if (state.tab === "health") await loadHealth();
      } catch (err) {
        document.querySelector(`#${state.tab}`).innerHTML = `<div class="panel"><div class="panel-body status-danger">${esc(err.message)}</div></div>`;
      }
    }

    document.querySelectorAll(".tab").forEach(tab => tab.addEventListener("click", async () => {
      document.querySelectorAll(".tab").forEach(t => t.classList.remove("active"));
      document.querySelectorAll(".section").forEach(s => s.classList.remove("active"));
      tab.classList.add("active");
      state.tab = tab.dataset.tab;
      document.getElementById(state.tab).classList.add("active");
      await refresh();
    }));
    document.getElementById("refresh").addEventListener("click", refresh);
    document.getElementById("workspace").addEventListener("change", refresh);
    document.getElementById("project").addEventListener("keydown", e => { if (e.key === "Enter") refresh(); });
    document.querySelectorAll("[data-action]").forEach(button => button.addEventListener("click", refresh));

    loadWorkspaces().then(loadOverview);
  </script>
</body>
</html>
""";
  }
}
