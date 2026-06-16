namespace AiMemory.Configuration;

public sealed class AiMemoryConfig
{
    public string ActiveWorkspace { get; set; } = "Default";
    public List<AiMemoryWorkspaceConfig> Workspaces { get; set; } = [];

    // Backward compatibility for config files created before multi-workspace support.
    public string WorkspaceName { get; set; } = "Default";
    public List<AiMemoryProjectConfig> Projects { get; set; } = [];

    public string Database { get; set; } = "ai_memory";
    public string DatabaseHost { get; set; } = "localhost";
    public int DatabasePort { get; set; } = 5432;
    public string DatabaseUser { get; set; } = Environment.UserName;
    public string? DatabasePassword { get; set; }
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "bge-m3";
    public string SemanticModel { get; set; } = "qwen2.5-coder:7b";
}

public sealed class AiMemoryWorkspaceConfig
{
    public string Name { get; set; } = "";
    public List<AiMemoryProjectConfig> Projects { get; set; } = [];
}

public sealed class AiMemoryProjectConfig
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}
