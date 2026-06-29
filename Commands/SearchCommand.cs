using AiMemory.Services;
using AiMemory.Configuration;

namespace AiMemory.Commands;

public static class SearchCommand
{
    public static async Task RunAsync(string query, int limit, string? db, string? ollama, string? model)
    {
        var config = await ConfigService.LoadAsync();
        var embedding = await new OllamaService(
            ConfigService.ResolveOllamaBaseUrl(config, ollama),
            ConfigService.ResolveEmbeddingModel(config, model)).EmbedAsync(query);
        await using var pg = new PgVectorService(ConfigService.ResolveConnectionString(config, db));
        var results = await pg.SearchAsync(embedding, query, limit);
        foreach (var r in results)
        {
            Console.WriteLine($"[{r.Distance:0.0000}] {r.Project}/{r.File} {(r.Symbol is null ? "" : ":: " + r.Symbol)}");
            Console.WriteLine(r.Content.Length > 600 ? r.Content[..600] + "..." : r.Content);
            Console.WriteLine(new string('-', 80));
        }
    }
}
