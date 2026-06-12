using AiMemory.Services;
using AiMemory.Configuration;

namespace AiMemory.Commands;

public static class IndexCommand
{
    public static async Task RunAsync(string? project, string? workspaceName, string? db, string? ollama, string? model)
    {
        var config = await ConfigService.LoadAsync();
        var workspace = ConfigService.GetWorkspace(config, workspaceName);
        if (workspace is null)
        {
            Console.WriteLine(string.IsNullOrWhiteSpace(workspaceName)
                ? "No active workspace configured. Run ai-memory setup or ai-memory workspace add <name>."
                : $"Workspace not found in configuration: {workspaceName}");
            return;
        }

        var projects = string.IsNullOrWhiteSpace(project)
            ? workspace.Projects
            : workspace.Projects.Where(p => p.Name.Equals(project, StringComparison.OrdinalIgnoreCase)).ToList();

        if (projects.Count == 0)
        {
            Console.WriteLine(string.IsNullOrWhiteSpace(project)
                ? $"No projects configured in workspace '{workspace.Name}'. Run ai-memory setup or ai-memory project add --workspace {workspace.Name}."
                : $"Project not found in workspace '{workspace.Name}': {project}");
            return;
        }

        var chunker = new ChunkingService();
        var ollamaService = new OllamaService(
            ConfigService.ResolveOllamaBaseUrl(config, ollama),
            ConfigService.ResolveEmbeddingModel(config, model));
        var pg = new PgVectorService(ConfigService.ResolveConnectionString(config, db));

        foreach (var configuredProject in projects)
        {
            var root = ConfigService.ExpandPath(configuredProject.Path);
            if (!Directory.Exists(root))
            {
                Console.WriteLine($"Skipping {configuredProject.Name}: directory does not exist: {root}");
                continue;
            }

            var projectName = $"{workspace.Name}/{configuredProject.Name}";
            Console.WriteLine($"Indexing {projectName}: {root}");
            foreach (var file in chunker.EnumerateFiles(root))
            {
                foreach (var chunk in chunker.ChunkFile(projectName, root, file))
                {
                    var embedding = await ollamaService.EmbedAsync(chunk.Content);
                    await pg.UpsertChunkAsync(chunk, embedding);
                }
                Console.WriteLine($"  indexed {Path.GetRelativePath(root, file)}");
            }
        }
    }
}
