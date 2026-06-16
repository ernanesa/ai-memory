using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using AiMemory.Configuration;
using Npgsql;

namespace AiMemory.Commands;

public static class SetupCommand
{
    private const string DefaultEmbeddingModel = "bge-m3";
    private const string DefaultSemanticModel = "qwen2.5-coder:7b";
    private const int MaxVisibleLogLines = 10;

    public static async Task RunAsync()
    {
        WriteTitle("AI Memory Setup");

        var config = await ConfigService.LoadAsync();
        var state = await DetectStateAsync(config, checkDatabase: true);

        PrintDetectedState(state);
        CollectConfiguration(config, state);
        state = await DetectStateAsync(config, checkDatabase: true);

        Console.WriteLine();
        CollectWorkspaces(config);

        var plan = CollectSetupPlan(config, state);

        Console.WriteLine();
        PrintPlan(plan);
        if (!PromptYesNo("Run setup actions now?", true))
        {
            await ConfigService.SaveAsync(config);
            WriteSuccess($"Configuration saved to {ConfigService.ConfigPath}");
            WriteWarning("Setup actions skipped.");
            return;
        }

        Console.WriteLine();
        WriteSection("Running setup actions");
        var results = await ExecutePlanAsync(config, plan);
        await ConfigService.SaveAsync(config);
        results.Add(SetupStepResult.Success("Configuration", $"Saved to {ConfigService.ConfigPath}"));

        Console.WriteLine();
        PrintSummary(results);
        WriteInfo("Next: ai-memory index");
    }

    private static async Task<SetupState> DetectStateAsync(AiMemoryConfig config, bool checkDatabase)
    {
        var platform = DetectPlatform();
        var packageManager = platform switch
        {
            SetupPlatform.MacOs => "brew",
            SetupPlatform.Linux => "apt-get",
            SetupPlatform.Windows => "winget",
            _ => null
        };

        var hasPackageManager = packageManager is not null && await CommandExistsAsync(packageManager);
        var hasPostgresClient = await CommandExistsAsync("psql");
        var hasPgConfig = await CommandExistsAsync("pg_config");
        var hasOllamaCommand = await ResolveOllamaCommandAsync() is not null;
        var hasSystemctl = platform == SetupPlatform.Linux && await CommandExistsAsync("systemctl");
        var hasGit = await CommandExistsAsync("git");
        var hasNmake = platform == SetupPlatform.Windows && await CommandExistsAsync("nmake");
        var hasClCompiler = platform == SetupPlatform.Windows && await CommandExistsAsync("cl");
        var hasPgVectorControlFile = hasPgConfig && await HasPgVectorControlFileAsync();
        var isOllamaReachable = await IsOllamaAvailableAsync(config.OllamaBaseUrl);
        var ollamaModels = isOllamaReachable ? await ListOllamaModelsAsync(config.OllamaBaseUrl) : [];
        var canConnectDatabase = checkDatabase && await CanConnectToDatabaseAsync(ConfigService.ResolveConnectionString(config));
        var hasPgVectorSupport = hasPgVectorControlFile || checkDatabase && await IsVectorExtensionAvailableAsync(config);

        return new SetupState(
            platform,
            packageManager,
            hasPackageManager,
            hasPostgresClient,
            hasPgVectorSupport,
            hasOllamaCommand,
            isOllamaReachable,
            ollamaModels,
            canConnectDatabase,
            hasSystemctl,
            hasGit,
            hasNmake,
            hasClCompiler);
    }

    private static void PrintDetectedState(SetupState state)
    {
        WriteSection("Detected environment");
        WriteInfo($"Platform: {GetPlatformDisplayName(state.Platform)}");
        if (state.PackageManager is not null)
        {
            WriteStatus(state.HasSupportedPackageManager, $"{state.PackageManager} found", $"{state.PackageManager} not found");
        }
        WriteStatus(state.HasPostgresClient, "PostgreSQL client found", "PostgreSQL client not found");
        WriteStatus(state.HasPgVectorSupport, "pgvector available", "pgvector not available");
        WriteStatus(state.HasOllamaCommand, "Ollama command found", "Ollama command not found");
        WriteStatus(state.IsOllamaReachable, "Ollama reachable", "Ollama not reachable");
        WriteStatus(state.CanConnectDatabase, "PostgreSQL database reachable", "PostgreSQL database not reachable");
        if (state.Platform == SetupPlatform.Linux)
        {
            WriteStatus(state.HasSystemctl, "systemctl found", "systemctl not found");
        }

        if (state.Platform == SetupPlatform.Windows)
        {
            var hasWindowsBuildTools = state.HasGit && state.HasNmake && state.HasClCompiler;
            WriteStatus(hasWindowsBuildTools, "Windows C++ build tools found for pgvector", "Windows C++ build tools not found for pgvector");
        }

        Console.WriteLine();
    }

    private static void CollectConfiguration(AiMemoryConfig config, SetupState state)
    {
        config.Database = Prompt("Database name or connection string", config.Database);
        if (!config.Database.Contains('='))
        {
            config.DatabaseHost = Prompt("PostgreSQL host", config.DatabaseHost);
            config.DatabasePort = PromptInt("PostgreSQL port", config.DatabasePort, defaultValue: 5432);
            config.DatabaseUser = Prompt("PostgreSQL user", GetSuggestedDatabaseUser(config.DatabaseUser, state.Platform));
            config.DatabasePassword = PromptPassword("PostgreSQL password - optional, leave empty for local trust/.pgpass", config.DatabasePassword);
        }
        else
        {
            WriteMuted("Database host/port/user/password prompts skipped because a full connection string was provided.");
        }
        config.OllamaBaseUrl = Prompt("Ollama base URL", config.OllamaBaseUrl);
        config.EmbeddingModel = PromptModel("Embedding model", state.OllamaModels, config.EmbeddingModel, DefaultEmbeddingModel);
        config.SemanticModel = PromptModel("Semantic extraction model", state.OllamaModels, config.SemanticModel, DefaultSemanticModel);
    }

    private static void CollectWorkspaces(AiMemoryConfig config)
    {
        var first = true;
        do
        {
            var defaultName = first ? config.ActiveWorkspace : null;
            var workspaceName = Prompt("Workspace name - label for a group of projects", defaultName);
            if (string.IsNullOrWhiteSpace(workspaceName))
            {
                break;
            }

            var workspace = ConfigService.GetOrCreateWorkspace(config, workspaceName);
            if (first)
            {
                config.ActiveWorkspace = workspace.Name;
            }

            WriteSection($"Projects for workspace '{workspace.Name}'");
            CollectProjects(workspace);
            first = false;
        }
        while (PromptYesNo("Configure another workspace?", false));
    }

    private static void CollectProjects(AiMemoryWorkspaceConfig workspace)
    {
        WriteMuted("Press Enter without a directory when you are done.");
        while (true)
        {
            var path = Prompt("Project directory");
            if (string.IsNullOrWhiteSpace(path))
            {
                break;
            }

            path = ConfigService.ExpandPath(path);
            var name = new DirectoryInfo(path).Name;
            WriteMuted($"Project name: {name}");

            var existing = workspace.Projects.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                workspace.Projects.Add(new AiMemoryProjectConfig { Name = name, Path = path });
            }
            else
            {
                existing.Path = path;
            }
        }
    }

    private static SetupPlan CollectSetupPlan(AiMemoryConfig config, SetupState state)
    {
        Console.WriteLine();
        WriteSection("Setup actions");

        var actions = state.Platform switch
        {
            SetupPlatform.MacOs => CollectMacSetupActions(state),
            SetupPlatform.Linux => CollectLinuxSetupActions(state),
            SetupPlatform.Windows => CollectWindowsSetupActions(state),
            _ => []
        };

        var databaseName = GetDatabaseName(config.Database);
        var createDatabase = !state.CanConnectDatabase
            && !string.IsNullOrWhiteSpace(databaseName)
            && PromptYesNo($"Create PostgreSQL database '{databaseName}' if needed?", true);

        var applySchema = PromptYesNo("Apply/update AI Memory database schema after database is reachable?", true);
        var modelsToPull = new List<string>();
        foreach (var model in new[] { config.EmbeddingModel, config.SemanticModel }
                     .Where(model => !string.IsNullOrWhiteSpace(model))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!state.OllamaModels.Any(m => m.Equals(model, StringComparison.OrdinalIgnoreCase)) &&
                PromptYesNo($"Pull Ollama model '{model}' if missing?", true))
            {
                modelsToPull.Add(model);
            }
        }

        return new SetupPlan(
            actions.ToArray(),
            createDatabase,
            applySchema,
            modelsToPull.ToArray());
    }

    private static void PrintPlan(SetupPlan plan)
    {
        WriteSection("Planned actions");
        foreach (var action in plan.Actions)
        {
            WriteBullet($"{action.Name}: {action.DisplayText ?? FormatCommand(action.FileName, action.Arguments)}");
        }

        if (plan.CreateDatabase)
        {
            WriteBullet("create PostgreSQL database if needed");
        }

        if (plan.ApplySchema)
        {
            WriteBullet("apply/update database schema");
        }

        if (plan.ModelsToPull.Length > 0)
        {
            WriteBullet($"pull missing Ollama model(s): {string.Join(", ", plan.ModelsToPull)}");
        }

        if (plan.IsEmpty)
        {
            WriteBullet("no automatic setup actions selected");
        }
    }

    private static async Task<List<SetupStepResult>> ExecutePlanAsync(AiMemoryConfig config, SetupPlan plan)
    {
        var results = new List<SetupStepResult>();

        foreach (var action in plan.Actions)
        {
            var result = await RunAndReportAsync(action);
            results.Add(ToStepResult(action.Name, result));
            if (action.PauseAfterMs > 0)
            {
                await Task.Delay(action.PauseAfterMs);
            }
        }

        if (plan.CreateDatabase)
        {
            results.Add(await CreateDatabaseIfNeededAsync(config));
        }

        if (plan.ApplySchema)
        {
            results.Add(await ApplySchemaAsync(config));
        }

        foreach (var model in plan.ModelsToPull)
        {
            results.Add(await PullModelIfNeededAsync(config.OllamaBaseUrl, model));
        }

        return results;
    }

    private static async Task<SetupStepResult> CreateDatabaseIfNeededAsync(AiMemoryConfig config)
    {
        var targetConnectionString = ConfigService.ResolveConnectionString(config);
        if (await CanConnectToDatabaseAsync(targetConnectionString))
        {
            var message = "PostgreSQL database already reachable";
            WriteSuccess(message);
            return SetupStepResult.Success("Create database", message);
        }

        var databaseName = GetDatabaseName(config.Database);
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            const string message = "Could not infer database name from connection string";
            WriteWarning(message);
            return SetupStepResult.Warning("Create database", message);
        }

        if (databaseName.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            const string message = "Target database is 'postgres' and is not reachable";
            WriteWarning(message);
            return SetupStepResult.Warning("Create database", message);
        }

        var adminConnectionString = BuildConnectionStringForDatabase(targetConnectionString, "postgres");
        try
        {
            await using var conn = new NpgsqlConnection(adminConnectionString);
            await conn.OpenAsync();

            await using var existsCmd = conn.CreateCommand();
            existsCmd.CommandText = "select exists (select 1 from pg_database where datname = @name)";
            existsCmd.Parameters.AddWithValue("name", databaseName);
            var exists = (bool)(await existsCmd.ExecuteScalarAsync() ?? false);
            if (!exists)
            {
                await using var createCmd = conn.CreateCommand();
                var quotedDatabaseName = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
                createCmd.CommandText = $"create database {quotedDatabaseName}";
                await createCmd.ExecuteNonQueryAsync();
            }

            var message = exists
                ? $"PostgreSQL database already exists: {databaseName}"
                : $"PostgreSQL database created: {databaseName}";
            WriteSuccess(message);
            return SetupStepResult.Success("Create database", message);
        }
        catch (Exception ex)
        {
            var message = $"Failed to create database: {ex.Message}";
            WriteWarning(message);
            return SetupStepResult.Warning("Create database", message);
        }
    }

    private static async Task<SetupStepResult> ApplySchemaAsync(AiMemoryConfig config)
    {
        var connectionString = ConfigService.ResolveConnectionString(config);
        if (!await CanConnectToDatabaseAsync(connectionString))
        {
            const string message = "Schema not applied because the database is not reachable";
            WriteWarning(message);
            return SetupStepResult.Warning("Apply schema", message);
        }

        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "sql");
        if (!Directory.Exists(schemaDirectory))
        {
            var schemaDirectoryMessage = $"Schema directory not found: {schemaDirectory}";
            WriteWarning(schemaDirectoryMessage);
            return SetupStepResult.Warning("Apply schema", schemaDirectoryMessage);
        }

        try
        {
            var schemaFiles = Directory.GetFiles(schemaDirectory, "*.sql")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (schemaFiles.Length == 0)
            {
                var noSchemaFilesMessage = $"No schema files found in {schemaDirectory}";
                WriteWarning(noSchemaFilesMessage);
                return SetupStepResult.Warning("Apply schema", noSchemaFilesMessage);
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            foreach (var schemaFile in schemaFiles)
            {
                var schema = await File.ReadAllTextAsync(schemaFile);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = schema;
                await cmd.ExecuteNonQueryAsync();
            }

            var schemaAppliedMessage = $"Database schema applied ({schemaFiles.Length} file(s))";
            WriteSuccess(schemaAppliedMessage);
            return SetupStepResult.Success("Apply schema", schemaAppliedMessage);
        }
        catch (Exception ex)
        {
            var schemaErrorMessage = $"Failed to apply schema: {ex.Message}";
            if (ex.Message.Contains("vector", StringComparison.OrdinalIgnoreCase) &&
                (ex.Message.Contains("control", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("extension", StringComparison.OrdinalIgnoreCase)))
            {
                schemaErrorMessage += OperatingSystem.IsWindows()
                    ? " Install pgvector in the PostgreSQL server, then rerun ai-memory setup."
                    : " Install pgvector for the PostgreSQL server, then rerun ai-memory setup.";
            }
            WriteWarning(schemaErrorMessage);
            return SetupStepResult.Warning("Apply schema", schemaErrorMessage);
        }
    }

    private static async Task<SetupStepResult> PullModelIfNeededAsync(string ollamaBaseUrl, string model)
    {
        var models = await ListOllamaModelsAsync(ollamaBaseUrl);
        if (models.Any(m => m.Equals(model, StringComparison.OrdinalIgnoreCase)))
        {
            var message = $"Ollama model found: {model}";
            WriteSuccess(message);
            return SetupStepResult.Success("Pull model", message);
        }

        var ollamaCommand = await ResolveOllamaCommandAsync();
        if (ollamaCommand is null)
        {
            const string message = "Ollama command not found. Model was not pulled.";
            WriteWarning(message);
            return SetupStepResult.Warning("Pull model", message);
        }

        var result = await RunAndReportAsync("Pull Ollama model", ollamaCommand, ["pull", model], streamOutput: true);
        return ToStepResult("Pull model", result);
    }

    private static string PromptModel(string label, IReadOnlyList<string> models, string current, string fallback)
    {
        var defaultModel = string.IsNullOrWhiteSpace(current) ? fallback : current;
        if (models.Count == 0)
        {
            return Prompt(label, defaultModel);
        }

        WriteSection("Available Ollama models");
        for (var i = 0; i < models.Count; i++)
        {
            WriteMuted($"{i + 1}) {models[i]}");
        }

        while (true)
        {
            var answer = Prompt(label, defaultModel);
            if (int.TryParse(answer, out var selected) && selected >= 1 && selected <= models.Count)
            {
                return models[selected - 1];
            }

            if (!string.IsNullOrWhiteSpace(answer))
            {
                return answer;
            }
        }
    }

    private static string Prompt(string label, string? defaultValue = null)
    {
        WriteColored(defaultValue is null ? $"{label}: " : $"{label} [{defaultValue}]: ", ConsoleColor.Cyan, newLine: false);
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? defaultValue ?? "" : value.Trim();
    }

    private static bool PromptYesNo(string label, bool defaultValue)
    {
        var suffix = defaultValue ? "Y/n" : "y/N";
        WriteColored($"{label} [{suffix}]: ", ConsoleColor.Cyan, newLine: false);
        var value = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)
            || value.Trim().StartsWith("s", StringComparison.OrdinalIgnoreCase);
    }

    private static int PromptInt(string label, int currentValue, int defaultValue)
    {
        var suggestedValue = currentValue > 0 ? currentValue : defaultValue;
        while (true)
        {
            var answer = Prompt(label, suggestedValue.ToString());
            if (int.TryParse(answer, out var parsed) && parsed > 0)
            {
                return parsed;
            }

            WriteWarning("Please enter a valid positive integer.");
        }
    }

    private static string? PromptPassword(string label, string? currentValue)
    {
        var suffix = string.IsNullOrWhiteSpace(currentValue) ? "empty" : "configured";
        WriteColored($"{label} [{suffix}]: ", ConsoleColor.Cyan, newLine: false);

        if (Console.IsInputRedirected)
        {
            var redirectedValue = Console.ReadLine();
            return string.IsNullOrWhiteSpace(redirectedValue) ? currentValue : redirectedValue.Trim();
        }

        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0)
                {
                    chars.RemoveAt(chars.Count - 1);
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                chars.Add(key.KeyChar);
            }
        }

        var value = new string(chars.ToArray());
        return string.IsNullOrWhiteSpace(value) ? currentValue : value;
    }

    private static async Task<bool> CommandExistsAsync(string command)
    {
        try
        {
            var locator = OperatingSystem.IsWindows() ? "where" : "which";
            var result = await RunProcessAsync(locator, [command], quiet: true);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsOllamaAvailableAsync(string baseUrl)
    {
        try
        {
            using var http = CreateOllamaClient(baseUrl);
            using var response = await http.GetAsync("api/version");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<string>> ListOllamaModelsAsync(string baseUrl)
    {
        try
        {
            using var http = CreateOllamaClient(baseUrl);
            var payload = await http.GetFromJsonAsync<OllamaTagsResponse>("api/tags");
            return payload?.Models.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Order().ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task<bool> CanConnectToDatabaseAsync(string connectionString)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetDatabaseName(string database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            return null;
        }

        if (!database.Contains('='))
        {
            return database.Trim();
        }

        try
        {
            return new NpgsqlConnectionStringBuilder(database).Database;
        }
        catch
        {
            return null;
        }
    }

    private static List<SetupAction> CollectMacSetupActions(SetupState state)
    {
        var actions = new List<SetupAction>();
        if (!state.HasSupportedPackageManager)
        {
            WriteWarning("Homebrew is required for automatic package installation on macOS.");
            return actions;
        }

        var installPackages = new List<string>();
        var installPostgres = !state.HasPostgresClient && PromptYesNo("Install PostgreSQL with Homebrew?", true);
        var installPgvector = !state.HasPgVectorSupport && PromptYesNo("Install pgvector with Homebrew?", true);
        var installOllama = !state.HasOllamaCommand && PromptYesNo("Install Ollama with Homebrew?", true);

        if (installPostgres)
        {
            installPackages.Add("postgresql");
        }

        if (installPgvector)
        {
            installPackages.Add("pgvector");
        }

        if (installOllama)
        {
            installPackages.Add("ollama");
        }

        if (installPackages.Count > 0)
        {
            actions.Add(new SetupAction(
                "Install Homebrew packages",
                "brew",
                ["install", .. installPackages.Distinct(StringComparer.OrdinalIgnoreCase)]));
        }

        if ((state.HasPostgresClient || installPostgres) && PromptYesNo("Start PostgreSQL with Homebrew services?", true))
        {
            actions.Add(new SetupAction("Start PostgreSQL", "brew", ["services", "start", "postgresql"], PauseAfterMs: 1000));
        }

        if ((state.HasOllamaCommand || installOllama) &&
            !state.IsOllamaReachable &&
            PromptYesNo("Start Ollama with Homebrew services?", true))
        {
            actions.Add(new SetupAction("Start Ollama", "brew", ["services", "start", "ollama"], PauseAfterMs: 1500));
        }

        return actions;
    }

    private static List<SetupAction> CollectLinuxSetupActions(SetupState state)
    {
        var actions = new List<SetupAction>();
        if (!state.HasSupportedPackageManager)
        {
            WriteWarning("Ubuntu automation requires apt/apt-get.");
            return actions;
        }

        var needsAptUpdate = false;
        var installPostgres = !state.HasPostgresClient && PromptYesNo("Install PostgreSQL with apt?", true);
        var installPgvector = !state.HasPgVectorSupport && PromptYesNo("Install pgvector for PostgreSQL?", true);
        var installOllama = !state.HasOllamaCommand && PromptYesNo("Install Ollama with the official Linux installer?", true);

        if (installPostgres || installPgvector)
        {
            needsAptUpdate = true;
        }

        if (needsAptUpdate)
        {
            actions.Add(new SetupAction("Refresh apt package index", "sudo", ["apt-get", "update"]));
        }

        if (installPostgres)
        {
            actions.Add(new SetupAction(
                "Install PostgreSQL",
                "sudo",
                ["apt-get", "install", "-y", "postgresql", "postgresql-contrib"]));
        }

        if (installPgvector)
        {
            const string script = """
set -euo pipefail
if ! command -v psql >/dev/null 2>&1; then
  echo "psql not found. Install PostgreSQL first."
  exit 1
fi
pg_major=$(psql --version | awk '{print $3}' | cut -d. -f1)
if [ -z "$pg_major" ]; then
  echo "Could not detect PostgreSQL major version."
  exit 1
fi
pkg="postgresql-${pg_major}-pgvector"
if apt-cache show "$pkg" >/dev/null 2>&1; then
  sudo apt-get install -y "$pkg"
  exit 0
fi
sudo apt-get install -y build-essential "postgresql-server-dev-${pg_major}" git
tmpdir=$(mktemp -d)
git clone --branch v0.8.2 https://github.com/pgvector/pgvector.git "$tmpdir/pgvector"
cd "$tmpdir/pgvector"
make
sudo make install
""";

            actions.Add(new SetupAction(
                "Install pgvector",
                "bash",
                ["-lc", script],
                StreamOutput: true,
                DisplayText: "bash -lc <install pgvector via apt/build>"));
        }

        if (installOllama)
        {
            actions.Add(new SetupAction(
                "Install Ollama",
                "bash",
                ["-lc", "curl -fsSL https://ollama.com/install.sh | sh"],
                StreamOutput: true,
                DisplayText: "bash -lc \"curl -fsSL https://ollama.com/install.sh | sh\""));
        }

        if ((state.HasPostgresClient || installPostgres) && state.HasSystemctl)
        {
            if (PromptYesNo("Start PostgreSQL with systemctl?", true))
            {
                actions.Add(new SetupAction("Start PostgreSQL", "sudo", ["systemctl", "start", "postgresql"], PauseAfterMs: 1000));
            }
        }
        else if (installPostgres || state.HasPostgresClient)
        {
            WriteWarning("Automatic PostgreSQL service start on Linux requires systemctl.");
        }

        if ((state.HasOllamaCommand || installOllama) && !state.IsOllamaReachable && state.HasSystemctl)
        {
            if (PromptYesNo("Start Ollama with systemctl?", true))
            {
                actions.Add(new SetupAction("Start Ollama", "sudo", ["systemctl", "start", "ollama"], PauseAfterMs: 1500));
            }
        }
        else if ((installOllama || state.HasOllamaCommand) && !state.HasSystemctl)
        {
            WriteWarning("Automatic Ollama service start on Linux requires systemctl.");
        }

        return actions;
    }

    private static List<SetupAction> CollectWindowsSetupActions(SetupState state)
    {
        var actions = new List<SetupAction>();
        if (!state.HasSupportedPackageManager)
        {
            WriteWarning("winget is required for automatic package installation on Windows.");
        }

        var installPostgres = false;
        var installOllama = false;
        if (state.HasSupportedPackageManager)
        {
            installPostgres = !state.HasPostgresClient &&
                              PromptYesNo("Install PostgreSQL 18 with winget (interactive installer)?", true);
            installOllama = !state.HasOllamaCommand &&
                            PromptYesNo("Install Ollama with winget?", true);

            if (installPostgres)
            {
                actions.Add(new SetupAction(
                    "Install PostgreSQL",
                    "winget",
                    ["install", "--id", "PostgreSQL.PostgreSQL.18", "-e", "--interactive", "--accept-source-agreements", "--accept-package-agreements"]));
            }

            if (installOllama)
            {
                actions.Add(new SetupAction(
                    "Install Ollama",
                    "winget",
                    ["install", "--id", "Ollama.Ollama", "-e", "--interactive", "--accept-source-agreements", "--accept-package-agreements"]));
            }
        }

        if (!state.HasPgVectorSupport)
        {
            if (state.HasGit && state.HasNmake && state.HasClCompiler)
            {
                if (PromptYesNo("Build/install pgvector for PostgreSQL now?", true))
                {
                    const string script = """
$pgRoot = Get-ChildItem "$env:ProgramFiles\PostgreSQL" -Directory -ErrorAction SilentlyContinue |
  Sort-Object Name -Descending |
  Select-Object -First 1
if (-not $pgRoot) { throw "PostgreSQL installation directory not found under Program Files\PostgreSQL." }
$tmp = Join-Path $env:TEMP ("pgvector-" + [guid]::NewGuid().ToString("N"))
git clone --branch v0.8.2 https://github.com/pgvector/pgvector.git $tmp
Push-Location $tmp
$env:PGROOT = $pgRoot.FullName
& nmake /F Makefile.win
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& nmake /F Makefile.win install
exit $LASTEXITCODE
""";

                    actions.Add(new SetupAction(
                        "Install pgvector",
                        "powershell",
                        ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
                        StreamOutput: true,
                        DisplayText: "powershell -Command <build pgvector with nmake>"));
                }
            }
            else
            {
                WriteWarning("pgvector is not available yet. On Windows, automatic installation needs git plus Visual Studio C++ tools with cl/nmake.");
            }
        }

        if (state.HasPostgresClient || installPostgres)
        {
            if (PromptYesNo("Start PostgreSQL Windows service?", true))
            {
                const string startPostgresScript = """
$service = Get-Service | Where-Object { $_.Name -like 'postgresql*' } | Sort-Object Name | Select-Object -First 1
if (-not $service) { throw "PostgreSQL Windows service not found." }
if ($service.Status -ne 'Running') { Start-Service -Name $service.Name }
""";

                actions.Add(new SetupAction(
                    "Start PostgreSQL",
                    "powershell",
                    ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", startPostgresScript],
                    PauseAfterMs: 1000,
                    DisplayText: "powershell -Command <start PostgreSQL service>"));
            }
        }

        if ((state.HasOllamaCommand || installOllama) && !state.IsOllamaReachable)
        {
            if (PromptYesNo("Start Ollama in the background?", true))
            {
                const string startOllamaScript = """
$candidates = @()
$cmd = Get-Command ollama -ErrorAction SilentlyContinue
if ($cmd) { $candidates += $cmd.Source }
$candidates += "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe"
$candidates += "$env:ProgramFiles\Ollama\ollama.exe"
$exe = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $exe) { throw "Ollama executable not found." }
Start-Process $exe -ArgumentList "serve" -WindowStyle Hidden
""";

                actions.Add(new SetupAction(
                    "Start Ollama",
                    "powershell",
                    ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", startOllamaScript],
                    PauseAfterMs: 1500,
                    DisplayText: "powershell -Command <start Ollama serve>"));
            }
        }

        return actions;
    }

    private static string GetSuggestedDatabaseUser(string currentValue, SetupPlatform platform)
    {
        if (platform == SetupPlatform.MacOs)
        {
            return string.IsNullOrWhiteSpace(currentValue) ? Environment.UserName : currentValue;
        }

        return string.IsNullOrWhiteSpace(currentValue) || currentValue.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)
            ? "postgres"
            : currentValue;
    }

    private static SetupPlatform DetectPlatform()
    {
        if (OperatingSystem.IsMacOS())
        {
            return SetupPlatform.MacOs;
        }

        if (OperatingSystem.IsLinux())
        {
            return SetupPlatform.Linux;
        }

        if (OperatingSystem.IsWindows())
        {
            return SetupPlatform.Windows;
        }

        return SetupPlatform.Unknown;
    }

    private static string GetPlatformDisplayName(SetupPlatform platform)
    {
        return platform switch
        {
            SetupPlatform.MacOs => "macOS",
            SetupPlatform.Linux => "Linux",
            SetupPlatform.Windows => "Windows",
            _ => "Unknown"
        };
    }

    private static async Task<bool> HasPgVectorControlFileAsync()
    {
        try
        {
            var result = await RunProcessAsync("pg_config", ["--sharedir"], quiet: true);
            if (result.ExitCode != 0)
            {
                return false;
            }

            var sharedDir = result.LogLines.LastOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(sharedDir))
            {
                return false;
            }

            var controlPath = Path.Combine(sharedDir, "extension", "vector.control");
            return File.Exists(controlPath);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsVectorExtensionAvailableAsync(AiMemoryConfig config)
    {
        var targetConnectionString = ConfigService.ResolveConnectionString(config);
        var connectionStrings = new[]
        {
            targetConnectionString,
            BuildConnectionStringForDatabase(targetConnectionString, "postgres")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var connectionString in connectionStrings)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "select exists (select 1 from pg_available_extensions where name = 'vector')";
                return (bool)(await cmd.ExecuteScalarAsync() ?? false);
            }
            catch
            {
            }
        }

        return false;
    }

    private static string BuildConnectionStringForDatabase(string connectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    private static async Task<string?> ResolveOllamaCommandAsync()
    {
        if (await CommandExistsAsync("ollama"))
        {
            return "ollama";
        }

        foreach (var candidate in GetOllamaCandidatePaths())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetOllamaCandidatePaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Ollama", "ollama.exe");
        }
    }

    private static string FormatCommand(string fileName, IReadOnlyList<string> arguments)
    {
        return arguments.Count == 0
            ? fileName
            : $"{fileName} {string.Join(' ', arguments)}";
    }

    private static HttpClient CreateOllamaClient(string baseUrl)
    {
        return new HttpClient(new SocketsHttpHandler { UseCookies = false })
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
        };
    }

    private static Task<ProcessResult> RunAndReportAsync(string title, string fileName, params string[] arguments)
    {
        return RunAndReportAsync(title, fileName, (IReadOnlyList<string>)arguments, environment: null);
    }

    private static async Task<ProcessResult> RunAndReportAsync(SetupAction action)
    {
        WriteInfo(action.Name);
        WriteMuted($"> {action.DisplayText ?? FormatCommand(action.FileName, action.Arguments)}");
        var result = await RunProcessAsync(action.FileName, action.Arguments, quiet: false, action.Environment, action.StreamOutput);

        if (!action.StreamOutput && result.LogLines.Count > 0)
        {
            WriteMuted($"Last {Math.Min(MaxVisibleLogLines, result.LogLines.Count)} log line(s):");
            foreach (var line in result.LogLines.TakeLast(MaxVisibleLogLines))
            {
                WriteMuted($"  {line}");
            }
        }

        if (result.ExitCode == 0)
        {
            WriteSuccess("Command completed");
        }
        else
        {
            WriteWarning($"Command exited with code {result.ExitCode}");
        }

        return result;
    }

    private static async Task<ProcessResult> RunAndReportAsync(string title, string fileName, IReadOnlyList<string> arguments)
    {
        return await RunAndReportAsync(title, fileName, arguments, environment: null);
    }

    private static async Task<ProcessResult> RunAndReportAsync(
        string title,
        string fileName,
        IReadOnlyList<string> arguments,
        bool streamOutput)
    {
        return await RunAndReportAsync(title, fileName, arguments, environment: null, streamOutput);
    }

    private static async Task<ProcessResult> RunAndReportAsync(
        string title,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment)
    {
        return await RunAndReportAsync(title, fileName, arguments, environment, streamOutput: false);
    }

    private static async Task<ProcessResult> RunAndReportAsync(
        string title,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment,
        bool streamOutput)
    {
        WriteInfo(title);
        WriteMuted($"> {fileName} {string.Join(' ', arguments)}");
        var result = await RunProcessAsync(fileName, arguments, quiet: false, environment, streamOutput);

        if (!streamOutput && result.LogLines.Count > 0)
        {
            WriteMuted($"Last {Math.Min(MaxVisibleLogLines, result.LogLines.Count)} log line(s):");
            foreach (var line in result.LogLines.TakeLast(MaxVisibleLogLines))
            {
                WriteMuted($"  {line}");
            }
        }

        if (result.ExitCode == 0)
        {
            WriteSuccess("Command completed");
        }
        else
        {
            WriteWarning($"Command exited with code {result.ExitCode}");
        }

        return result;
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, bool quiet)
    {
        return await RunProcessAsync(fileName, arguments, quiet, environment: null);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        bool quiet,
        IReadOnlyDictionary<string, string?>? environment)
    {
        return await RunProcessAsync(fileName, arguments, quiet, environment, streamOutput: false);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        bool quiet,
        IReadOnlyDictionary<string, string?>? environment,
        bool streamOutput)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    process.StartInfo.Environment[key] = value;
                }
            }
        }

        var logLines = new List<string>();
        process.Start();

        if (streamOutput)
        {
            var outputTask = StreamProcessOutputAsync(process.StandardOutput, Console.Out, quiet, logLines);
            var errorTask = StreamProcessOutputAsync(process.StandardError, Console.Error, quiet, logLines);
            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);
            if (!quiet)
            {
                Console.WriteLine();
            }
        }
        else
        {
            process.OutputDataReceived += (_, e) => CaptureLogLine(e.Data, quiet, logLines);
            process.ErrorDataReceived += (_, e) => CaptureLogLine(e.Data, quiet, logLines);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
        }

        return new ProcessResult(process.ExitCode, logLines);
    }

    private static async Task StreamProcessOutputAsync(
        TextReader reader,
        TextWriter writer,
        bool quiet,
        List<string> logLines)
    {
        var buffer = new char[1024];
        var currentLine = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            if (!quiet)
            {
                await writer.WriteAsync(buffer, 0, read);
                await writer.FlushAsync();
            }

            CaptureLogText(buffer.AsSpan(0, read), quiet, logLines, currentLine);
        }

        if (currentLine.Length > 0)
        {
            CaptureLogLine(currentLine.ToString(), quiet, logLines);
        }
    }

    private static void CaptureLogText(ReadOnlySpan<char> text, bool quiet, List<string> logLines, StringBuilder currentLine)
    {
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n')
            {
                if (currentLine.Length > 0)
                {
                    CaptureLogLine(currentLine.ToString(), quiet, logLines);
                    currentLine.Clear();
                }
                continue;
            }

            currentLine.Append(ch);
        }
    }

    private static void CaptureLogLine(string? line, bool quiet, List<string> logLines)
    {
        if (line is null)
        {
            return;
        }

        lock (logLines)
        {
            logLines.Add(line);
            if (!quiet && logLines.Count > MaxVisibleLogLines * 20)
            {
                logLines.RemoveRange(0, logLines.Count - MaxVisibleLogLines);
            }
        }
    }

    private static SetupStepResult ToStepResult(string name, ProcessResult result)
    {
        return result.ExitCode == 0
            ? SetupStepResult.Success(name, "Completed")
            : SetupStepResult.Warning(name, $"Exited with code {result.ExitCode}");
    }

    private static void PrintSummary(IReadOnlyList<SetupStepResult> results)
    {
        WriteTitle("Setup Summary");
        if (results.Count == 0)
        {
            WriteWarning("No automatic setup actions were executed.");
            return;
        }

        foreach (var result in results)
        {
            var color = result.Status switch
            {
                SetupStepStatus.Success => ConsoleColor.Green,
                SetupStepStatus.Warning => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };
            var label = result.Status switch
            {
                SetupStepStatus.Success => "[ok]",
                SetupStepStatus.Warning => "[warn]",
                _ => "[fail]"
            };
            WriteColored($"{label} {result.Name}: {result.Message}", color);
        }
    }

    private static void WriteTitle(string text)
    {
        WriteColored("==================================", ConsoleColor.DarkCyan);
        WriteColored(text, ConsoleColor.Cyan);
        WriteColored("==================================", ConsoleColor.DarkCyan);
        Console.WriteLine();
    }

    private static void WriteSection(string text)
    {
        WriteColored(text, ConsoleColor.Cyan);
    }

    private static void WriteStatus(bool ok, string success, string warning)
    {
        if (ok)
        {
            WriteSuccess(success);
        }
        else
        {
            WriteWarning(warning);
        }
    }

    private static void WriteSuccess(string text)
    {
        WriteColored($"[ok] {text}", ConsoleColor.Green);
    }

    private static void WriteWarning(string text)
    {
        WriteColored($"[warn] {text}", ConsoleColor.Yellow);
    }

    private static void WriteInfo(string text)
    {
        WriteColored(text, ConsoleColor.White);
    }

    private static void WriteMuted(string text)
    {
        WriteColored(text, ConsoleColor.DarkGray);
    }

    private static void WriteBullet(string text)
    {
        WriteColored($"- {text}", ConsoleColor.White);
    }

    private static void WriteColored(string text, ConsoleColor color, bool newLine = true)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        if (newLine)
        {
            Console.WriteLine(text);
        }
        else
        {
            Console.Write(text);
        }
        Console.ForegroundColor = previous;
    }

    private sealed record SetupState(
        SetupPlatform Platform,
        string? PackageManager,
        bool HasSupportedPackageManager,
        bool HasPostgresClient,
        bool HasPgVectorSupport,
        bool HasOllamaCommand,
        bool IsOllamaReachable,
        IReadOnlyList<string> OllamaModels,
        bool CanConnectDatabase,
        bool HasSystemctl,
        bool HasGit,
        bool HasNmake,
        bool HasClCompiler);

    private sealed record SetupPlan(
        SetupAction[] Actions,
        bool CreateDatabase,
        bool ApplySchema,
        string[] ModelsToPull)
    {
        public bool IsEmpty =>
            Actions.Length == 0 &&
            !CreateDatabase &&
            !ApplySchema &&
            ModelsToPull.Length == 0;
    }

    private sealed record SetupAction(
        string Name,
        string FileName,
        string[] Arguments,
        IReadOnlyDictionary<string, string?>? Environment = null,
        bool StreamOutput = false,
        int PauseAfterMs = 0,
        string? DisplayText = null);

    private enum SetupPlatform
    {
        Unknown,
        MacOs,
        Linux,
        Windows
    }

    private enum SetupStepStatus
    {
        Success,
        Warning,
        Failure
    }

    private sealed record SetupStepResult(SetupStepStatus Status, string Name, string Message)
    {
        public static SetupStepResult Success(string name, string message) => new(SetupStepStatus.Success, name, message);
        public static SetupStepResult Warning(string name, string message) => new(SetupStepStatus.Warning, name, message);
    }

    private sealed record ProcessResult(int ExitCode, IReadOnlyList<string> LogLines);
    private sealed record OllamaTagsResponse([property: JsonPropertyName("models")] OllamaModel[] Models);
    private sealed record OllamaModel([property: JsonPropertyName("name")] string Name);
}
