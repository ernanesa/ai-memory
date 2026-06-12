using AiMemory.Configuration;

namespace AiMemory.Commands;

public static class McpCommand
{
    public static async Task RunAsync(string? db, string? ollama, string? model)
    {
        var config = await ConfigService.LoadAsync();
        _ = ConfigService.ResolveConnectionString(config, db);
        _ = ConfigService.ResolveOllamaBaseUrl(config, ollama);
        _ = ConfigService.ResolveEmbeddingModel(config, model);

        Console.Error.WriteLine("MCP placeholder. Next step: implement tools search_code, search_business_rules and find_related_files.");
        // Keep process alive so IDEs can start it during configuration tests.
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}
