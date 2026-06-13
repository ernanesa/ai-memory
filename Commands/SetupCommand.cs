using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AiMemory.Configuration;
using Npgsql;

namespace AiMemory.Commands;

public static class SetupCommand
{
    private const string DefaultModel = "bge-m3";
    private const int MaxVisibleLogLines = 10;

    public static async Task RunAsync()
    {
        WriteTitle("AI Memory Setup");

        var config = await ConfigService.LoadAsync();
        var state = await DetectStateAsync(config, checkDatabase: true);

        PrintDetectedState(state);
        CollectConfiguration(config, state);
        state = state with { CanConnectDatabase = await CanConnectToDatabaseAsync(ConfigService.ResolveConnectionString(config)) };

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
        var hasBrew = await CommandExistsAsync("brew");
        var hasPostgresClient = await CommandExistsAsync("psql");
        var hasCreatedb = await CommandExistsAsync("createdb");
        var hasOllamaCommand = await CommandExistsAsync("ollama");
        var hasPgVectorFormula = hasBrew && (await RunProcessAsync("brew", ["list", "--formula", "pgvector"], quiet: true)).ExitCode == 0;
        var isOllamaReachable = await IsOllamaAvailableAsync(config.OllamaBaseUrl);
        var ollamaModels = isOllamaReachable ? await ListOllamaModelsAsync(config.OllamaBaseUrl) : [];
        var canConnectDatabase = checkDatabase && await CanConnectToDatabaseAsync(ConfigService.ResolveConnectionString(config));

        return new SetupState(
            hasBrew,
            hasPostgresClient,
            hasCreatedb,
            hasPgVectorFormula,
            hasOllamaCommand,
            isOllamaReachable,
            ollamaModels,
            canConnectDatabase);
    }

    private static void PrintDetectedState(SetupState state)
    {
        WriteSection("Detected environment");
        WriteStatus(state.HasBrew, "Homebrew found", "Homebrew not found");
        WriteStatus(state.HasPostgresClient, "PostgreSQL client found", "PostgreSQL client not found");
        WriteStatus(state.HasPgVectorFormula, "pgvector formula installed", "pgvector formula not found");
        WriteStatus(state.HasOllamaCommand, "Ollama command found", "Ollama command not found");
        WriteStatus(state.IsOllamaReachable, "Ollama reachable", "Ollama not reachable");
        WriteStatus(state.CanConnectDatabase, "PostgreSQL database reachable", "PostgreSQL database not reachable");
        Console.WriteLine();
    }

    private static void CollectConfiguration(AiMemoryConfig config, SetupState state)
    {
        config.Database = Prompt("Database name or connection string", config.Database);
        if (!config.Database.Contains('='))
        {
            config.DatabaseUser = Prompt("PostgreSQL user", config.DatabaseUser);
            config.DatabasePassword = PromptPassword("PostgreSQL password - optional, leave empty for local trust/.pgpass", config.DatabasePassword);
        }
        else
        {
            WriteMuted("Database user/password prompts skipped because a full connection string was provided.");
        }
        config.OllamaBaseUrl = Prompt("Ollama base URL", config.OllamaBaseUrl);
        config.EmbeddingModel = PromptModel(state.OllamaModels, config.EmbeddingModel);
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

        var installPackages = new List<string>();
        if (!state.HasBrew)
        {
            WriteWarning("Homebrew is required for automatic package installation.");
        }
        else
        {
            if (!state.HasPostgresClient && PromptYesNo("Install PostgreSQL with Homebrew?", true))
            {
                installPackages.Add("postgresql");
            }

            if (!state.HasPgVectorFormula && PromptYesNo("Install pgvector with Homebrew?", true))
            {
                installPackages.Add("pgvector");
            }

            if (!state.HasOllamaCommand && PromptYesNo("Install Ollama with Homebrew?", true))
            {
                installPackages.Add("ollama");
            }
        }

        var startPostgres = state.HasBrew && PromptYesNo("Start PostgreSQL with Homebrew services?", true);
        var startOllama = state.HasBrew && !state.IsOllamaReachable && PromptYesNo("Start Ollama with Homebrew services?", true);

        var databaseName = GetDatabaseName(config.Database);
        var createDatabase = !state.CanConnectDatabase
            && !string.IsNullOrWhiteSpace(databaseName)
            && PromptYesNo($"Create PostgreSQL database '{databaseName}' if needed?", true);

        var applySchema = PromptYesNo("Apply/update AI Memory database schema after database is reachable?", true);
        var pullModel = !state.OllamaModels.Any(m => m.Equals(config.EmbeddingModel, StringComparison.OrdinalIgnoreCase))
            && PromptYesNo($"Pull Ollama model '{config.EmbeddingModel}' if missing?", true);

        return new SetupPlan(
            installPackages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            startPostgres,
            startOllama,
            createDatabase,
            applySchema,
            pullModel);
    }

    private static void PrintPlan(SetupPlan plan)
    {
        WriteSection("Planned actions");
        if (plan.InstallBrewPackages.Length > 0)
        {
            WriteBullet($"brew install {string.Join(' ', plan.InstallBrewPackages)}");
        }

        if (plan.StartPostgres)
        {
            WriteBullet("brew services start postgresql");
        }

        if (plan.StartOllama)
        {
            WriteBullet("brew services start ollama");
        }

        if (plan.CreateDatabase)
        {
            WriteBullet("create PostgreSQL database if needed");
        }

        if (plan.ApplySchema)
        {
            WriteBullet("apply/update database schema");
        }

        if (plan.PullModel)
        {
            WriteBullet("pull Ollama embedding model if missing");
        }

        if (plan.IsEmpty)
        {
            WriteBullet("no automatic setup actions selected");
        }
    }

    private static async Task<List<SetupStepResult>> ExecutePlanAsync(AiMemoryConfig config, SetupPlan plan)
    {
        var results = new List<SetupStepResult>();

        if (plan.InstallBrewPackages.Length > 0)
        {
            var result = await RunAndReportAsync("Install Homebrew packages", "brew", ["install", .. plan.InstallBrewPackages]);
            results.Add(ToStepResult("Install packages", result));
        }

        if (plan.StartPostgres)
        {
            var result = await RunAndReportAsync("Start PostgreSQL", "brew", "services", "start", "postgresql");
            results.Add(ToStepResult("Start PostgreSQL", result));
            await Task.Delay(1000);
        }

        if (plan.StartOllama)
        {
            var result = await RunAndReportAsync("Start Ollama", "brew", "services", "start", "ollama");
            results.Add(ToStepResult("Start Ollama", result));
            await Task.Delay(1500);
        }

        if (plan.CreateDatabase)
        {
            results.Add(await CreateDatabaseIfNeededAsync(config));
        }

        if (plan.ApplySchema)
        {
            results.Add(await ApplySchemaAsync(config));
        }

        if (plan.PullModel)
        {
            results.Add(await PullModelIfNeededAsync(config.OllamaBaseUrl, config.EmbeddingModel));
        }

        return results;
    }

    private static async Task<SetupStepResult> CreateDatabaseIfNeededAsync(AiMemoryConfig config)
    {
        var connectionString = ConfigService.ResolveConnectionString(config);
        if (await CanConnectToDatabaseAsync(connectionString))
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

        if (!await CommandExistsAsync("createdb"))
        {
            const string message = "createdb not found. Database was not created.";
            WriteWarning(message);
            return SetupStepResult.Warning("Create database", message);
        }

        var result = await RunAndReportAsync(
            "Create database",
            "createdb",
            ["-U", config.DatabaseUser, databaseName],
            BuildPostgresEnvironment(config));
        return ToStepResult("Create database", result);
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

        if (!await CommandExistsAsync("ollama"))
        {
            const string message = "Ollama command not found. Model was not pulled.";
            WriteWarning(message);
            return SetupStepResult.Warning("Pull model", message);
        }

        var result = await RunAndReportAsync("Pull Ollama model", "ollama", "pull", model);
        return ToStepResult("Pull model", result);
    }

    private static string PromptModel(IReadOnlyList<string> models, string current)
    {
        var defaultModel = string.IsNullOrWhiteSpace(current) ? DefaultModel : current;
        if (models.Count == 0)
        {
            return Prompt("Embedding model", defaultModel);
        }

        WriteSection("Available Ollama models");
        for (var i = 0; i < models.Count; i++)
        {
            WriteMuted($"{i + 1}) {models[i]}");
        }

        while (true)
        {
            var answer = Prompt("Embedding model", defaultModel);
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
            var result = await RunProcessAsync(command, ["--version"], quiet: true);
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

    private static Dictionary<string, string?> BuildPostgresEnvironment(AiMemoryConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.DatabasePassword))
        {
            return [];
        }

        return new Dictionary<string, string?> { ["PGPASSWORD"] = config.DatabasePassword };
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

    private static async Task<ProcessResult> RunAndReportAsync(string title, string fileName, IReadOnlyList<string> arguments)
    {
        return await RunAndReportAsync(title, fileName, arguments, environment: null);
    }

    private static async Task<ProcessResult> RunAndReportAsync(
        string title,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment)
    {
        WriteInfo(title);
        WriteMuted($"> {fileName} {string.Join(' ', arguments)}");
        var result = await RunProcessAsync(fileName, arguments, quiet: false, environment);

        if (result.LogLines.Count > 0)
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
        process.OutputDataReceived += (_, e) => CaptureLogLine(e.Data, quiet, logLines);
        process.ErrorDataReceived += (_, e) => CaptureLogLine(e.Data, quiet, logLines);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, logLines);
    }

    private static void CaptureLogLine(string? line, bool quiet, List<string> logLines)
    {
        if (line is null)
        {
            return;
        }

        logLines.Add(line);
        if (!quiet && logLines.Count > MaxVisibleLogLines * 20)
        {
            logLines.RemoveRange(0, logLines.Count - MaxVisibleLogLines);
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
        bool HasBrew,
        bool HasPostgresClient,
        bool HasCreatedb,
        bool HasPgVectorFormula,
        bool HasOllamaCommand,
        bool IsOllamaReachable,
        IReadOnlyList<string> OllamaModels,
        bool CanConnectDatabase);

    private sealed record SetupPlan(
        string[] InstallBrewPackages,
        bool StartPostgres,
        bool StartOllama,
        bool CreateDatabase,
        bool ApplySchema,
        bool PullModel)
    {
        public bool IsEmpty =>
            InstallBrewPackages.Length == 0 &&
            !StartPostgres &&
            !StartOllama &&
            !CreateDatabase &&
            !ApplySchema &&
            !PullModel;
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
