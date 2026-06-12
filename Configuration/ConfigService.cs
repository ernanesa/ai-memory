using System.Text.Json;
using Npgsql;

namespace AiMemory.Configuration;

public static class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string ConfigDirectory
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(string.IsNullOrWhiteSpace(home) ? Directory.GetCurrentDirectory() : home, ".ai-memory");
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static async Task<AiMemoryConfig> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPath))
        {
            return CreateDefault();
        }

        await using var stream = File.OpenRead(ConfigPath);
        var config = await JsonSerializer.DeserializeAsync<AiMemoryConfig>(stream, JsonOptions, ct);
        return Normalize(config ?? CreateDefault());
    }

    public static async Task SaveAsync(AiMemoryConfig config, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ConfigDirectory);
        await using var stream = File.Create(ConfigPath);
        await JsonSerializer.SerializeAsync(stream, ToPersistedConfig(Normalize(config)), JsonOptions, ct);
        await stream.WriteAsync("\n"u8.ToArray(), ct);
    }

    public static bool Exists() => File.Exists(ConfigPath);

    public static string ResolveConnectionString(AiMemoryConfig config, string? dbOverride = null)
    {
        var value = FirstNonEmpty(dbOverride, Environment.GetEnvironmentVariable("AI_MEMORY_DB"), config.Database, "ai_memory");
        if (value.Contains('='))
        {
            return value;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = value,
            Username = FirstNonEmpty(Environment.GetEnvironmentVariable("AI_MEMORY_DB_USER"), config.DatabaseUser, Environment.UserName)
        };

        var password = FirstNonEmptyOrNull(Environment.GetEnvironmentVariable("AI_MEMORY_DB_PASSWORD"), config.DatabasePassword);
        if (!string.IsNullOrWhiteSpace(password))
        {
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    public static string ResolveOllamaBaseUrl(AiMemoryConfig config, string? ollamaOverride = null)
    {
        return FirstNonEmpty(ollamaOverride, Environment.GetEnvironmentVariable("AI_MEMORY_OLLAMA"), config.OllamaBaseUrl, "http://localhost:11434");
    }

    public static string ResolveEmbeddingModel(AiMemoryConfig config, string? modelOverride = null)
    {
        return FirstNonEmpty(modelOverride, Environment.GetEnvironmentVariable("AI_MEMORY_EMBED_MODEL"), config.EmbeddingModel, "bge-m3");
    }

    private static AiMemoryConfig CreateDefault()
    {
        return new AiMemoryConfig
        {
            ActiveWorkspace = "Default",
            Workspaces = [new AiMemoryWorkspaceConfig { Name = "Default" }],
            Database = Environment.GetEnvironmentVariable("AI_MEMORY_DB") ?? "ai_memory",
            DatabaseUser = Environment.GetEnvironmentVariable("AI_MEMORY_DB_USER") ?? Environment.UserName,
            DatabasePassword = Environment.GetEnvironmentVariable("AI_MEMORY_DB_PASSWORD"),
            OllamaBaseUrl = Environment.GetEnvironmentVariable("AI_MEMORY_OLLAMA") ?? "http://localhost:11434",
            EmbeddingModel = Environment.GetEnvironmentVariable("AI_MEMORY_EMBED_MODEL") ?? "bge-m3"
        };
    }

    private static AiMemoryConfig Normalize(AiMemoryConfig config)
    {
        config.WorkspaceName = FirstNonEmpty(config.WorkspaceName, "Default");
        config.ActiveWorkspace = FirstNonEmpty(config.ActiveWorkspace, config.WorkspaceName, "Default");
        config.Database = FirstNonEmpty(config.Database, "ai_memory");
        config.DatabaseUser = FirstNonEmpty(config.DatabaseUser, Environment.UserName);
        config.DatabasePassword = FirstNonEmptyOrNull(config.DatabasePassword);
        config.OllamaBaseUrl = FirstNonEmpty(config.OllamaBaseUrl, "http://localhost:11434");
        config.EmbeddingModel = FirstNonEmpty(config.EmbeddingModel, "bge-m3");
        config.Projects ??= [];
        config.Workspaces ??= [];

        foreach (var project in config.Projects)
        {
            project.Name = project.Name.Trim();
            project.Path = ExpandPath(project.Path.Trim());
        }

        config.Projects = config.Projects
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Path))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (config.Workspaces.Count == 0)
        {
            config.Workspaces.Add(new AiMemoryWorkspaceConfig
            {
                Name = config.WorkspaceName,
                Projects = config.Projects
            });
        }

        foreach (var workspace in config.Workspaces)
        {
            workspace.Name = FirstNonEmpty(workspace.Name, "Default");
            workspace.Projects ??= [];

            foreach (var project in workspace.Projects)
            {
                project.Name = project.Name.Trim();
                project.Path = ExpandPath(project.Path.Trim());
            }

            workspace.Projects = workspace.Projects
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Path))
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        config.Workspaces = config.Workspaces
            .Where(w => !string.IsNullOrWhiteSpace(w.Name))
            .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (config.Workspaces.Count > 1)
        {
            config.Workspaces.RemoveAll(w =>
                w.Name.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                w.Projects.Count == 0 &&
                !config.ActiveWorkspace.Equals("Default", StringComparison.OrdinalIgnoreCase));
        }

        if (config.Workspaces.All(w => !w.Name.Equals(config.ActiveWorkspace, StringComparison.OrdinalIgnoreCase)))
        {
            config.ActiveWorkspace = config.Workspaces.FirstOrDefault()?.Name ?? "Default";
        }

        var activeWorkspace = GetWorkspace(config, config.ActiveWorkspace);
        config.WorkspaceName = activeWorkspace?.Name ?? config.ActiveWorkspace;
        config.Projects = activeWorkspace?.Projects ?? [];

        return config;
    }

    public static AiMemoryWorkspaceConfig? GetWorkspace(AiMemoryConfig config, string? workspaceName = null)
    {
        var name = FirstNonEmpty(workspaceName, config.ActiveWorkspace, config.WorkspaceName, "Default");
        return config.Workspaces.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static AiMemoryWorkspaceConfig GetOrCreateWorkspace(AiMemoryConfig config, string workspaceName)
    {
        var name = FirstNonEmpty(workspaceName, "Default");
        var workspace = config.Workspaces.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (workspace is not null)
        {
            return workspace;
        }

        workspace = new AiMemoryWorkspaceConfig { Name = name };
        config.Workspaces.Add(workspace);
        return workspace;
    }

    private static PersistedAiMemoryConfig ToPersistedConfig(AiMemoryConfig config)
    {
        return new PersistedAiMemoryConfig
        {
            ActiveWorkspace = config.ActiveWorkspace,
            Workspaces = config.Workspaces,
            Database = config.Database,
            DatabaseUser = config.DatabaseUser,
            DatabasePassword = config.DatabasePassword,
            OllamaBaseUrl = config.OllamaBaseUrl,
            EmbeddingModel = config.EmbeddingModel
        };
    }

    public static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }

        return Path.GetFullPath(path);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }

    private static string? FirstNonEmptyOrNull(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private sealed class PersistedAiMemoryConfig
    {
        public string ActiveWorkspace { get; set; } = "Default";
        public List<AiMemoryWorkspaceConfig> Workspaces { get; set; } = [];
        public string Database { get; set; } = "ai_memory";
        public string DatabaseUser { get; set; } = Environment.UserName;
        public string? DatabasePassword { get; set; }
        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
        public string EmbeddingModel { get; set; } = "bge-m3";
    }
}
