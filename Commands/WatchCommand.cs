namespace AiMemory.Commands;

public static class WatchCommand
{
    public static async Task RunAsync(string? db, string? ollama, string? model)
    {
        Console.WriteLine("MVP placeholder: use 'ai-memory index' first.");
        Console.WriteLine("Next implementation: FileSystemWatcher with debounce and per-file reindex.");
        await Task.Delay(1);
    }
}
