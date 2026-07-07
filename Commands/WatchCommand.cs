using AiMemory.Configuration;
using AiMemory.Services;
using System.Diagnostics;

namespace AiMemory.Commands
{
    public static class WatchCommand
    {
        public static async Task RunAsync(string? db, string? ollama, string? model)
        {
            var config = await ConfigService.LoadAsync();
            var workspace = ConfigService.GetWorkspace(config, null);
            if (workspace is null)
            {
                Console.WriteLine("No active workspace configured. Run ai-memory setup or ai-memory workspace add <name>.");
                return;
            }

            var connectionString = ConfigService.ResolveConnectionString(config, db);
            var ollamaBaseUrl = ConfigService.ResolveOllamaBaseUrl(config, ollama);
            var embeddingModel = ConfigService.ResolveEmbeddingModel(config, model);

            var pendingFiles = new HashSet<string>(StringComparer.Ordinal);
            var pendingLock = new object();
            using var cts = new CancellationTokenSource();
            using var debounceTimer = new System.Threading.Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);

            var watchers = new List<FileSystemWatcher>();
            var chunker = new ChunkingService();

            foreach (var project in workspace.Projects)
            {
                var root = ConfigService.ExpandPath(project.Path);
                if (!Directory.Exists(root))
                {
                    Console.WriteLine($"Skipping {project.Name}: directory does not exist: {root}");
                    continue;
                }

                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };

                void OnFileChanged(object? sender, FileSystemEventArgs e)
                {
                    var ext = Path.GetExtension(e.Name ?? "");
                    if (!chunker.ShouldWatchExtension(ext)) return;

                    var fullPath = e.FullPath;
                    lock (pendingLock)
                    {
                        pendingFiles.Add(fullPath);
                        debounceTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
                    }
                }

                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += (s, e) =>
                {
                    var oldExt = Path.GetExtension(e.OldName ?? "");
                    var newExt = Path.GetExtension(e.Name ?? "");
                    if (chunker.ShouldWatchExtension(oldExt) || chunker.ShouldWatchExtension(newExt))
                    {
                        lock (pendingLock)
                        {
                            if (chunker.ShouldWatchExtension(oldExt))
                                pendingFiles.Add(e.OldFullPath);
                            if (chunker.ShouldWatchExtension(newExt))
                                pendingFiles.Add(e.FullPath);
                            debounceTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
                        }
                    }
                };
                watcher.Error += (s, e) => Console.Error.WriteLine($"Watcher error in {project.Name}: {e.GetException().Message}");

                watchers.Add(watcher);
                Console.WriteLine($"Watching {workspace.Name}/{project.Name}: {root}");
            }

            debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);

            Console.WriteLine("Watching for changes. Press Ctrl+C to stop.");

            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(500, cts.Token);
                List<string> toProcess;
                lock (pendingLock)
                {
                    if (pendingFiles.Count == 0) continue;
                    toProcess = pendingFiles.ToList();
                    pendingFiles.Clear();
                }

                Console.WriteLine($"Reindexing {toProcess.Count} file(s)...");
                try
                {
                    await ReindexFilesAsync(toProcess, workspace.Name, config, connectionString, ollamaBaseUrl, embeddingModel, cts.Token);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Reindex error: {ex.Message}");
                }
            }

            foreach (var w in watchers) w.Dispose();
        }

        private static async Task ReindexFilesAsync(List<string> files, string workspaceName, AiMemoryConfig config, string connectionString, string ollamaBaseUrl, string embeddingModel, CancellationToken ct)
        {
            var chunker = new ChunkingService();
            var ollamaService = new OllamaService(ollamaBaseUrl, embeddingModel);
            await using var pg = new PgVectorService(connectionString);

            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;
                var ext = Path.GetExtension(file);
                if (!chunker.ShouldWatchExtension(ext)) continue;

                var config2 = await ConfigService.LoadAsync();
                var ws = ConfigService.GetWorkspace(config2, null);
                if (ws is null) continue;

                foreach (var project in ws.Projects)
                {
                    var root = ConfigService.ExpandPath(project.Path);
                    if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                    var relative = Path.GetRelativePath(root, file);
                    try
                    {
                        foreach (var chunk in chunker.ChunkFile(project.Name, root, file))
                        {
                            var contextualText = ContextualChunkingService.GetContextualContent(chunk);
                            var embedding = await ollamaService.EmbedAsync(contextualText, ct);
                            await pg.UpsertChunkAsync(workspaceName, chunk, embedding, ct);
                        }
                        Console.Error.WriteLine($"  reindexed {relative}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  failed {relative}: {ex.Message}");
                    }
                    break;
                }
            }
        }
    }
}