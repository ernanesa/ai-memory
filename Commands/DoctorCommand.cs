using System.Text.Json;
using AiMemory.Configuration;
using Npgsql;

namespace AiMemory.Commands
{
    public static class DoctorCommand
    {
        private static readonly string[] RequiredExtensions = ["vector", "pgcrypto"];
        private static readonly string[] OptionalExtensions = ["uuid-ossp"];
        private static readonly string[] RequiredTables =
        [
            "ai_workspaces",
            "ai_projects",
            "ai_workspace_projects",
            "ai_chunks",
            "ai_business_rules",
            "ai_knowledge",
            "ai_extraction_chunk_state",
            "ai_symbols",
            "ai_symbol_relations"
        ];

        private static readonly IReadOnlyDictionary<string, string[]> RequiredColumns =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["ai_workspaces"] = ["id", "name"],
                ["ai_projects"] = ["id", "name", "root_path"],
                ["ai_workspace_projects"] = ["workspace_id", "project_id"],
                ["ai_chunks"] =
                [
                    "id", "project_id", "file_path", "language", "chunk_type", "symbol_name", "content",
                    "content_hash", "embedding", "updated_at", "search_vector"
                ],
                ["ai_business_rules"] =
                [
                    "id", "project_id", "chunk_id", "title", "description", "source_file", "symbol_name",
                    "status", "evidence", "content_hash", "confidence", "embedding", "updated_at", "search_vector"
                ],
                ["ai_knowledge"] =
                [
                    "id", "project_id", "chunk_id", "kind", "title", "content", "source", "symbol_name",
                    "status", "evidence", "content_hash", "confidence", "embedding", "updated_at", "search_vector"
                ],
                ["ai_extraction_chunk_state"] =
                [
                    "chunk_id", "stage", "content_hash", "status", "processed_at", "error", "updated_at"
                ],
                ["ai_symbols"] = ["id", "project_id", "chunk_id", "kind", "full_name", "file_path", "line_start", "line_end"],
                ["ai_symbol_relations"] = ["source_id", "target_id", "relation"]
            };

        public static async Task<int> RunAsync(bool json = false, bool strict = false, bool noNetwork = false)
        {
            var checks = new List<DoctorCheck>();
            var configExists = ConfigService.Exists();
            var config = await ConfigService.LoadAsync();

            Add(checks, configExists, "config", "Configuration file", ConfigService.ConfigPath, "Configuration file not found. Run ai-memory setup.");
            AddConfigPasswordChecks(checks, configExists);
            AddWorkspaceChecks(checks, config);
            await AddPostgresChecksAsync(checks, config);
            if (noNetwork)
            {
                Add(checks, DoctorStatus.Warn, "ollama", "Ollama API", "skipped", "Ollama check skipped by --no-network.");
            }
            else
            {
                await AddOllamaCheckAsync(checks, config);
            }

            if (json)
            {
                PrintJsonReport(checks);
            }
            else
            {
                PrintReport(checks);
            }

            return checks.Any(check => check.Status == DoctorStatus.Fail || strict && check.Status == DoctorStatus.Warn) ? 1 : 0;
        }

        private static void AddWorkspaceChecks(ICollection<DoctorCheck> checks, AiMemoryConfig config)
        {
            Add(checks, config.Workspaces.Count > 0, "config", "Configured workspaces", config.Workspaces.Count.ToString(), "No workspaces configured. Run ai-memory setup.");
            Add(checks, !string.IsNullOrWhiteSpace(config.ActiveWorkspace), "config", "Active workspace", config.ActiveWorkspace, "Active workspace is empty.");

            var activeWorkspaceExists = config.Workspaces.Any(workspace =>
                workspace.Name.Equals(config.ActiveWorkspace, StringComparison.OrdinalIgnoreCase));
            Add(checks, activeWorkspaceExists, "config", "Active workspace is configured", config.ActiveWorkspace, $"Active workspace is not configured: {config.ActiveWorkspace}");

            var totalProjects = config.Workspaces.Sum(workspace => workspace.Projects.Count);
            Add(checks, totalProjects > 0, "config", "Configured projects", totalProjects.ToString(), "No projects configured. Run ai-memory project add.");

            foreach (var workspace in config.Workspaces)
            {
                Add(checks, !string.IsNullOrWhiteSpace(workspace.Name), "workspace", "Workspace name", workspace.Name, "Workspace has an empty name.");

                foreach (var project in workspace.Projects)
                {
                    var label = $"{workspace.Name}/{project.Name}";
                    Add(checks, !string.IsNullOrWhiteSpace(project.Name), "path", $"Project name {label}", project.Name, $"Project in workspace {workspace.Name} has an empty name.");
                    Add(checks, !string.IsNullOrWhiteSpace(project.Path), "path", $"Project path {label}", project.Path, $"Project {label} has an empty path.");

                    if (!string.IsNullOrWhiteSpace(project.Path))
                    {
                        Add(checks, Directory.Exists(project.Path), "path", $"Project path {label}", project.Path, $"Project path does not exist for {label}: {project.Path}");
                    }
                }
            }
        }

        private static void AddConfigPasswordChecks(ICollection<DoctorCheck> checks, bool configExists)
        {
            if (!configExists)
            {
                return;
            }

            Add(checks, ConfigService.HasRestrictiveConfigPermissions(), "security", "Configuration file permissions", ConfigService.ConfigPath,
                "Configuration file is readable by group/other users. Run ai-memory setup or chmod 600 ~/.ai-memory/config.json.");

            try
            {
                using var stream = File.OpenRead(ConfigService.ConfigPath);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    Add(checks, DoctorStatus.Fail, "config", "Configuration JSON", "invalid", "Configuration root must be a JSON object.");
                    return;
                }

                if (TryGetStringProperty(doc.RootElement, "databasePassword", out var password) && !string.IsNullOrWhiteSpace(password))
                {
                    Add(checks, DoctorStatus.Warn, "security", "Database password in config", "present", "Database password is stored in config.json. Prefer AI_MEMORY_DB_PASSWORD or a local secret store.");
                }

                if (TryGetStringProperty(doc.RootElement, "database", out var database) && ContainsPasswordKey(database))
                {
                    Add(checks, DoctorStatus.Warn, "security", "Connection string password in config", "present", "Database connection string in config.json includes a password. Prefer AI_MEMORY_DB or AI_MEMORY_DB_PASSWORD outside the config file.");
                }
            }
            catch (Exception ex)
            {
                Add(checks, DoctorStatus.Fail, "config", "Configuration JSON", ConfigService.ConfigPath, $"Configuration file could not be parsed: {ex.Message}");
            }
        }

        private static async Task AddPostgresChecksAsync(ICollection<DoctorCheck> checks, AiMemoryConfig config)
        {
            try
            {
                await using var conn = new NpgsqlConnection(ConfigService.ResolveConnectionString(config));
                await conn.OpenAsync();
                await ExecuteScalarAsync(conn, "SELECT 1");
                Add(checks, DoctorStatus.Ok, "postgres", "SELECT 1", "ok", "PostgreSQL connection ok.");

                await AddExtensionChecksAsync(checks, conn);
                await AddTableChecksAsync(checks, conn);
                await AddColumnChecksAsync(checks, conn);
                await AddSchemaMigrationChecksAsync(checks, conn);
            }
            catch (Exception ex)
            {
                Add(checks, DoctorStatus.Fail, "postgres", "Connection", "failed", $"PostgreSQL connection failed: {ex.Message}");
                Add(checks, DoctorStatus.Warn, "postgres", "Schema checks", "skipped", "Schema checks skipped because the database is not accessible.");
            }
        }

        private static async Task AddExtensionChecksAsync(ICollection<DoctorCheck> checks, NpgsqlConnection conn)
        {
            var installed = await ReadSetAsync(conn, "SELECT extname FROM pg_extension;");
            foreach (var extension in RequiredExtensions)
            {
                Add(checks, installed.Contains(extension), "postgres", $"Extension {extension}", extension, $"Required PostgreSQL extension is missing: {extension}");
            }

            foreach (var extension in OptionalExtensions)
            {
                Add(checks, installed.Contains(extension) ? DoctorStatus.Ok : DoctorStatus.Warn, "postgres", $"Optional extension {extension}", extension,
                    installed.Contains(extension) ? "Optional PostgreSQL extension is installed." : $"Optional PostgreSQL extension is not installed: {extension}");
            }
        }

        private static async Task AddTableChecksAsync(ICollection<DoctorCheck> checks, NpgsqlConnection conn)
        {
            var existingTables = await ReadSetAsync(conn, """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = current_schema()
                  AND table_type = 'BASE TABLE';
                """);

            foreach (var table in RequiredTables)
            {
                Add(checks, existingTables.Contains(table), "postgres", $"Table {table}", table, $"Required table is missing: {table}");
            }
        }

        private static async Task AddColumnChecksAsync(ICollection<DoctorCheck> checks, NpgsqlConnection conn)
        {
            var columns = await ReadColumnsAsync(conn);

            foreach (var (table, requiredColumns) in RequiredColumns)
            {
                if (!columns.TryGetValue(table, out var existingColumns))
                {
                    continue;
                }

                foreach (var column in requiredColumns)
                {
                    Add(checks, existingColumns.Contains(column), "postgres", $"Column {table}.{column}", column, $"Required column is missing: {table}.{column}");
                }
            }
        }

        private static async Task AddSchemaMigrationChecksAsync(ICollection<DoctorCheck> checks, NpgsqlConnection conn)
        {
            var table = await FirstExistingTableAsync(conn, "schema_migrations", "ai_schema_migrations");
            if (table is null)
            {
                Add(checks, DoctorStatus.Warn, "postgres", "Table schema_migrations", "missing", "Migration metadata table schema_migrations or ai_schema_migrations is missing.");
                return;
            }

            var count = await ExecuteScalarAsync(conn, $"SELECT count(*) FROM {table};");
            Add(checks, DoctorStatus.Ok, "postgres", $"Table {table}", $"{count} row(s)", "Migration metadata table exists.");

            var projectIdType = await ExecuteScalarAsync(conn, """
                SELECT data_type
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'ai_projects'
                  AND column_name = 'id';
                """);
            Add(checks, string.Equals(projectIdType?.ToString(), "integer", StringComparison.OrdinalIgnoreCase), "postgres", "ai_projects.id type",
                projectIdType?.ToString() ?? "missing", "ai_projects.id must be integer for the current symbol graph runtime.");
        }

        private static async Task AddOllamaCheckAsync(ICollection<DoctorCheck> checks, AiMemoryConfig config)
        {
            try
            {
                using var http = new HttpClient(new SocketsHttpHandler { UseCookies = false })
                {
                    BaseAddress = new Uri(ConfigService.ResolveOllamaBaseUrl(config).TrimEnd('/') + "/"),
                    Timeout = TimeSpan.FromSeconds(3)
                };
                using var response = await http.GetAsync("api/version");
                Add(checks, response.IsSuccessStatusCode ? DoctorStatus.Ok : DoctorStatus.Fail, "ollama", "Ollama API", ((int)response.StatusCode).ToString(),
                    response.IsSuccessStatusCode ? "Ollama connection ok." : $"Ollama returned HTTP {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                Add(checks, DoctorStatus.Warn, "ollama", "Ollama API", "unavailable", $"Ollama connection failed: {ex.Message}");
            }
        }

        private static async Task<object?> ExecuteScalarAsync(NpgsqlConnection conn, string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync();
        }

        private static async Task<HashSet<string>> ReadSetAsync(NpgsqlConnection conn, string sql)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }

        private static async Task<Dictionary<string, HashSet<string>>> ReadColumnsAsync(NpgsqlConnection conn)
        {
            var columns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT table_name, column_name
                FROM information_schema.columns
                WHERE table_schema = current_schema();
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                var column = reader.GetString(1);
                if (!columns.TryGetValue(table, out var tableColumns))
                {
                    tableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    columns[table] = tableColumns;
                }

                tableColumns.Add(column);
            }

            return columns;
        }

        private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string table)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT to_regclass($1) IS NOT NULL;";
            cmd.Parameters.AddWithValue(table);
            return (bool)(await cmd.ExecuteScalarAsync() ?? false);
        }

        private static async Task<string?> FirstExistingTableAsync(NpgsqlConnection conn, params string[] tables)
        {
            foreach (var table in tables)
            {
                if (await TableExistsAsync(conn, table))
                {
                    return table;
                }
            }

            return null;
        }

        private static void PrintReport(IReadOnlyCollection<DoctorCheck> checks)
        {
            Console.WriteLine("AI Memory Doctor");
            Console.WriteLine();

            foreach (var check in checks)
            {
                Console.WriteLine($"[{ToLabel(check.Status)}] {check.Name}: {check.Message}");
            }

            Console.WriteLine();
            Console.WriteLine($"Summary: {checks.Count(check => check.Status == DoctorStatus.Ok)} ok, {checks.Count(check => check.Status == DoctorStatus.Warn)} warning(s), {checks.Count(check => check.Status == DoctorStatus.Fail)} failure(s)");
        }

        private static void PrintJsonReport(IReadOnlyCollection<DoctorCheck> checks)
        {
            var payload = new
            {
                ok = checks.All(check => check.Status != DoctorStatus.Fail),
                counts = new
                {
                    ok = checks.Count(check => check.Status == DoctorStatus.Ok),
                    warnings = checks.Count(check => check.Status == DoctorStatus.Warn),
                    failures = checks.Count(check => check.Status == DoctorStatus.Fail)
                },
                checks = checks.Select(check => new
                {
                    check.Category,
                    check.Name,
                    status = ToLabel(check.Status),
                    check.Value,
                    check.Message
                })
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void Add(ICollection<DoctorCheck> checks, bool ok, string category, string name, string value, string failure)
        {
            Add(checks, ok ? DoctorStatus.Ok : DoctorStatus.Fail, category, name, value, ok ? value : failure);
        }

        private static void Add(ICollection<DoctorCheck> checks, DoctorStatus status, string category, string name, string value, string message)
        {
            checks.Add(new DoctorCheck(category, name, status, value, message));
        }

        private static bool TryGetStringProperty(JsonElement element, string name, out string? value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                {
                    value = property.Value.GetString();
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static bool ContainsPasswordKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.Contains('='))
            {
                return false;
            }

            try
            {
                var builder = new NpgsqlConnectionStringBuilder(value);
                return !string.IsNullOrWhiteSpace(builder.Password);
            }
            catch
            {
                return value.Contains("password=", StringComparison.OrdinalIgnoreCase) ||
                       value.Contains("pwd=", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string ToLabel(DoctorStatus status) => status switch
        {
            DoctorStatus.Ok => "ok",
            DoctorStatus.Warn => "warn",
            DoctorStatus.Fail => "fail",
            _ => "unknown"
        };

        private sealed record DoctorCheck(string Category, string Name, DoctorStatus Status, string Value, string Message);

        private enum DoctorStatus
        {
            Ok,
            Warn,
            Fail
        }
    }
}
