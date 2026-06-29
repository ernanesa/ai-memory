using AiMemory.Services;
using AiMemory.Configuration;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AiMemory.Commands;

public static class IndexCommand
{
    private static readonly HashSet<string> StageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chunks",
        "rules",
        "knowledge"
    };

    public static async Task RunAsync(
        string[] stages,
        string? project,
        string? workspaceName,
        string? db,
        string? ollama,
        string? model,
        bool semantic,
        string? semanticModel,
        bool refresh,
        int? candidateLimit)
    {
        var plan = ResolveStages(stages, ref project);
        if (plan is null)
        {
            return;
        }

        if (candidateLimit <= 0)
        {
            Console.WriteLine("--candidate-limit must be greater than zero.");
            return;
        }

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

        Console.WriteLine($"Index stages: {string.Join(", ", plan.Value.Stages.Select(s => s.ToString().ToLowerInvariant()))}");
        Console.WriteLine($"Workspace: {workspace.Name}");
        if (semantic && (plan.Value.Stages.Contains(IndexStage.Rules) || plan.Value.Stages.Contains(IndexStage.Knowledge)))
        {
            Console.WriteLine($"Semantic extraction: enabled ({ConfigService.ResolveSemanticModel(config, semanticModel)})");
        }
        if (refresh && (plan.Value.Stages.Contains(IndexStage.Rules) || plan.Value.Stages.Contains(IndexStage.Knowledge)))
        {
            Console.WriteLine("Extraction refresh: enabled");
        }
        if (!string.IsNullOrWhiteSpace(project))
        {
            Console.WriteLine($"Project: {project}");
        }
        Console.WriteLine();

        if (plan.Value.Stages.Contains(IndexStage.Chunks))
        {
            await IndexChunksAsync(projects, workspace.Name, config, db, ollama, model);
        }

        if (plan.Value.Stages.Contains(IndexStage.Rules))
        {
            await IndexRulesAsync(projects, workspace.Name, config, db, ollama, model, semantic, semanticModel, refresh, candidateLimit);
        }

        if (plan.Value.Stages.Contains(IndexStage.Knowledge))
        {
            await IndexKnowledgeAsync(projects, workspace.Name, config, db, ollama, model, semantic, semanticModel, refresh, candidateLimit);
        }
    }

    private static async Task IndexChunksAsync(
        IReadOnlyList<AiMemoryProjectConfig> projects,
        string workspaceName,
        AiMemoryConfig config,
        string? db,
        string? ollama,
        string? model)
    {
        var chunker = new ChunkingService();
        var ollamaService = new OllamaService(
            ConfigService.ResolveOllamaBaseUrl(config, ollama),
            ConfigService.ResolveEmbeddingModel(config, model));
        await using var pg = new PgVectorService(ConfigService.ResolveConnectionString(config, db));
        var removedGeneratedChunks = await pg.DeleteEntityFrameworkMigrationChunksAsync(
            workspaceName,
            projects.Select(p => p.Name).ToArray());
        if (removedGeneratedChunks > 0)
        {
            Console.WriteLine($"Removed existing Entity Framework migration chunks: {removedGeneratedChunks:N0}");
            Console.WriteLine();
        }

        var removedTestChunks = await pg.DeleteTestChunksAsync(
            workspaceName,
            projects.Select(p => p.Name).ToArray());
        if (removedTestChunks > 0)
        {
            Console.WriteLine($"Removed existing test chunks: {removedTestChunks:N0}");
            Console.WriteLine();
        }

        foreach (var configuredProject in projects)
        {
            var root = ConfigService.ExpandPath(configuredProject.Path);
            if (!Directory.Exists(root))
            {
                Console.WriteLine($"Skipping {configuredProject.Name}: directory does not exist: {root}");
                continue;
            }

            Console.WriteLine($"Indexing chunks for {workspaceName}/{configuredProject.Name}: {root}");
            var files = chunker.EnumerateFiles(root).ToList();
            using var progress = new ChunkIndexProgressReporter("chunk files", files.Count);
            var indexedChunks = 0;

            foreach (var file in files)
            {
                var relativeFile = Path.GetRelativePath(root, file);
                progress.BeforeItem(relativeFile);

                foreach (var chunk in chunker.ChunkFile(configuredProject.Name, root, file))
                {
                    try
                    {
                        var contextualText = ContextualChunkingService.GetContextualContent(chunk);
                        var embedding = await ollamaService.EmbedAsync(contextualText);
                        await pg.UpsertChunkAsync(workspaceName, chunk, embedding);
                        indexedChunks++;
                        progress.AfterChunk(indexedChunks);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to index chunk from {chunk.FilePath}" +
                            $"{(chunk.SymbolName is null ? "" : $" ({chunk.SymbolName})")} " +
                            $"with length {chunk.Content.Length}.",
                            ex);
                    }
                }

                progress.AfterItem(indexedChunks);
            }

            progress.Complete(indexedChunks);

            try
            {
                var projectId = await pg.GetProjectIdByNameAsync(configuredProject.Name);
                if (projectId > 0)
                {
                    Console.WriteLine($"Building symbol graph for {configuredProject.Name}...");
                    var graphService = new SymbolGraphService(pg, projectId, root);
                    await graphService.BuildGraphAsync(files);
                    Console.WriteLine("Symbol graph built successfully.");
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: failed to build symbol graph: {ex.Message}");
            }
        }
    }

    private static async Task IndexRulesAsync(
        IReadOnlyList<AiMemoryProjectConfig> projects,
        string workspaceName,
        AiMemoryConfig config,
        string? db,
        string? ollama,
        string? model,
        bool semantic,
        string? semanticModel,
        bool refresh,
        int? candidateLimit)
    {
        Console.WriteLine($"Indexing rules for workspace '{workspaceName}' ({projects.Count} project(s)).");
        if (semantic)
        {
            Console.WriteLine("  semantic mode: using semantic extraction for selected chunks.");
        }
        if (refresh)
        {
            Console.WriteLine("  refresh mode: reprocessing matching chunks even when already processed.");
        }
        var ollamaBaseUrl = ConfigService.ResolveOllamaBaseUrl(config, ollama);
        await using var pg = new PgVectorService(ConfigService.ResolveConnectionString(config, db));
        var ollamaService = new OllamaService(
            ollamaBaseUrl,
            ConfigService.ResolveEmbeddingModel(config, model));
        var semanticService = semantic
            ? new OllamaService(ollamaBaseUrl, ConfigService.ResolveSemanticModel(config, semanticModel))
            : null;

        var projectNames = projects.Select(p => p.Name).ToArray();
        var stats = await pg.GetRuleExtractionStatsAsync(workspaceName, projectNames, semantic);
        var chunks = await pg.GetChunksForRuleExtractionAsync(workspaceName, projectNames, candidateLimit, semantic, refresh);
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var extracted = 0;

        PrintCandidateScope(stats, chunks.Count, candidateLimit, refresh);

        using var progress = new ProgressReporter("rule chunks", chunks.Count);
        foreach (var chunk in chunks)
        {
            progress.BeforeItem($"{chunk.File}{(chunk.Symbol is null ? "" : $"::{chunk.Symbol}")}");
            var chunkFailed = false;
            var chunkError = "";
            var candidates = new List<ExtractedBusinessRule>();
            try
            {
                candidates.AddRange(RuleExtractionService.ExtractBusinessRules(chunk));
                if (semanticService is not null)
                {
                    candidates.AddRange(await RuleExtractionService.ExtractSemanticBusinessRulesAsync(semanticService, chunk));
                }

                candidates = RuleExtractionService.DeduplicateBusinessRules(candidates).ToList();
                extracted += candidates.Count;
            }
            catch (Exception ex)
            {
                chunkFailed = true;
                chunkError = ex.Message;
                progress.WriteMessage($"  failed rule extraction {chunk.File}: {ex.Message}");
            }

            if (chunkFailed)
            {
                await pg.MarkExtractionChunkFailedAsync(chunk.Id, "rules", chunk.ContentHash, chunkError);
                progress.AfterItem(inserted, updated, skipped);
                continue;
            }

            if (candidates.Count == 0)
            {
                await pg.MarkExtractionChunkProcessedAsync(chunk.Id, "rules", chunk.ContentHash);
                progress.AfterItem(inserted, updated, skipped);
                continue;
            }

            try
            {
                foreach (var candidate in candidates)
                {
                    progress.UpdateCurrent($"{candidate.SourceFile}{(candidate.SymbolName is null ? "" : $"::{candidate.SymbolName}")}");
                    var embedding = await ollamaService.EmbedAsync($"{candidate.Title}\n{candidate.Description}\n{candidate.Evidence}");
                    var result = await pg.UpsertBusinessRuleCandidateAsync(candidate, embedding);
                    if (result.Action == "inserted") inserted++;
                    else if (result.Action == "updated") updated++;
                    else skipped++;
                }
            }
            catch (Exception ex)
            {
                chunkFailed = true;
                chunkError = ex.Message;
                skipped += candidates.Count;
                progress.WriteMessage($"  failed rule chunk {chunk.File}: {ex.Message}");
            }

            if (chunkFailed)
            {
                await pg.MarkExtractionChunkFailedAsync(chunk.Id, "rules", chunk.ContentHash, chunkError);
            }
            else
            {
                await pg.MarkExtractionChunkProcessedAsync(chunk.Id, "rules", chunk.ContentHash);
            }

            progress.AfterItem(inserted, updated, skipped);
        }
        progress.Complete(inserted, updated, skipped);

        Console.WriteLine($"  extracted rule candidates: {extracted:N0}");
        Console.WriteLine($"  rules inserted: {inserted:N0}");
        Console.WriteLine($"  rules updated:  {updated:N0}");
        Console.WriteLine($"  rules skipped:  {skipped:N0}");
        Console.WriteLine("  review status preserved: candidates were not auto-accepted; rejected rules were not reactivated.");
    }

    private static async Task IndexKnowledgeAsync(
        IReadOnlyList<AiMemoryProjectConfig> projects,
        string workspaceName,
        AiMemoryConfig config,
        string? db,
        string? ollama,
        string? model,
        bool semantic,
        string? semanticModel,
        bool refresh,
        int? candidateLimit)
    {
        Console.WriteLine($"Indexing knowledge for workspace '{workspaceName}' ({projects.Count} project(s)).");
        if (semantic)
        {
            Console.WriteLine("  semantic mode: using semantic extraction for selected chunks.");
        }
        if (refresh)
        {
            Console.WriteLine("  refresh mode: reprocessing matching chunks even when already processed.");
        }
        var ollamaBaseUrl = ConfigService.ResolveOllamaBaseUrl(config, ollama);
        await using var pg = new PgVectorService(ConfigService.ResolveConnectionString(config, db));
        var ollamaService = new OllamaService(
            ollamaBaseUrl,
            ConfigService.ResolveEmbeddingModel(config, model));
        var semanticService = semantic
            ? new OllamaService(ollamaBaseUrl, ConfigService.ResolveSemanticModel(config, semanticModel))
            : null;

        var projectNames = projects.Select(p => p.Name).ToArray();
        var stats = await pg.GetKnowledgeExtractionStatsAsync(workspaceName, projectNames, semantic);
        var chunks = await pg.GetChunksForKnowledgeExtractionAsync(workspaceName, projectNames, candidateLimit, semantic, refresh);
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var extracted = 0;

        PrintCandidateScope(stats, chunks.Count, candidateLimit, refresh);

        using var progress = new ProgressReporter("knowledge chunks", chunks.Count);
        foreach (var chunk in chunks)
        {
            progress.BeforeItem($"{chunk.File}{(chunk.Symbol is null ? "" : $"::{chunk.Symbol}")}");
            var chunkFailed = false;
            var chunkError = "";
            var candidates = new List<ExtractedKnowledge>();
            try
            {
                candidates.AddRange(KnowledgeExtractionService.ExtractKnowledge(chunk));
                if (semanticService is not null)
                {
                    candidates.AddRange(await KnowledgeExtractionService.ExtractSemanticKnowledgeAsync(semanticService, chunk));
                }

                candidates = KnowledgeExtractionService.DeduplicateKnowledge(candidates).ToList();
                extracted += candidates.Count;
            }
            catch (Exception ex)
            {
                chunkFailed = true;
                chunkError = ex.Message;
                progress.WriteMessage($"  failed knowledge extraction {chunk.File}: {ex.Message}");
            }

            if (chunkFailed)
            {
                await pg.MarkExtractionChunkFailedAsync(chunk.Id, "knowledge", chunk.ContentHash, chunkError);
                progress.AfterItem(inserted, updated, skipped);
                continue;
            }

            if (candidates.Count == 0)
            {
                await pg.MarkExtractionChunkProcessedAsync(chunk.Id, "knowledge", chunk.ContentHash);
                progress.AfterItem(inserted, updated, skipped);
                continue;
            }

            try
            {
                foreach (var candidate in candidates)
                {
                    progress.UpdateCurrent($"{candidate.Source}{(candidate.SymbolName is null ? "" : $"::{candidate.SymbolName}")}");
                    var embedding = await ollamaService.EmbedAsync($"{candidate.Kind}\n{candidate.Title}\n{candidate.Content}\n{candidate.Evidence}");
                    var result = await pg.UpsertKnowledgeCandidateAsync(candidate, embedding);
                    if (result.Action == "inserted") inserted++;
                    else if (result.Action == "updated") updated++;
                    else skipped++;
                }
            }
            catch (Exception ex)
            {
                chunkFailed = true;
                chunkError = ex.Message;
                skipped += candidates.Count;
                progress.WriteMessage($"  failed knowledge chunk {chunk.File}: {ex.Message}");
            }

            if (chunkFailed)
            {
                await pg.MarkExtractionChunkFailedAsync(chunk.Id, "knowledge", chunk.ContentHash, chunkError);
            }
            else
            {
                await pg.MarkExtractionChunkProcessedAsync(chunk.Id, "knowledge", chunk.ContentHash);
            }

            progress.AfterItem(inserted, updated, skipped);
        }
        progress.Complete(inserted, updated, skipped);

        Console.WriteLine($"  extracted knowledge candidates: {extracted:N0}");
        Console.WriteLine($"  knowledge inserted: {inserted:N0}");
        Console.WriteLine($"  knowledge updated:  {updated:N0}");
        Console.WriteLine($"  knowledge skipped:  {skipped:N0}");
        Console.WriteLine("  review status preserved: candidates were not auto-accepted; rejected records were not reactivated.");
    }

    private static void PrintCandidateScope(ExtractionStats stats, int selectedCandidateChunks, int? candidateLimit, bool refresh)
    {
        var rows = new (string Metric, string Value)[]
        {
            ("total chunks in scope", stats.TotalChunks.ToString("N0")),
            ("matching candidates", stats.CandidateChunks.ToString("N0")),
            ("already processed", stats.ProcessedCandidateChunks.ToString("N0")),
            ("pending new", stats.PendingCandidateChunks.ToString("N0")),
            ("failed", stats.FailedCandidateChunks.ToString("N0")),
            ("changed hash", stats.ChangedCandidateChunks.ToString("N0")),
            ("actionable", stats.ActionableCandidateChunks.ToString("N0")),
            ("selected", selectedCandidateChunks.ToString("N0")),
            ("refresh", refresh ? "enabled" : "disabled"),
            ("candidate limit", candidateLimit is null ? "none" : candidateLimit.Value.ToString("N0"))
        };

        Console.WriteLine("  Candidate scope");
        Console.WriteLine($"  {"Metric",-27} {"Value",12}");
        Console.WriteLine($"  {new string('-', 27)} {new string('-', 12)}");
        foreach (var row in rows)
        {
            Console.WriteLine($"  {row.Metric,-27} {row.Value,12}");
        }

        if (candidateLimit is null && selectedCandidateChunks > 0)
        {
            WriteWarning(
                $"Processing all {selectedCandidateChunks:N0} candidate chunks. " +
                "This can take a long time because each candidate generates an embedding. " +
                "Use --candidate-limit <n> to process a smaller batch.");
        }
    }

    private static void WriteWarning(string message)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine($"  warning: {message}");
            return;
        }

        Console.WriteLine($"\u001b[33m  warning: {message}\u001b[0m");
    }

    private static IndexPlan? ResolveStages(string[] rawStages, ref string? project)
    {
        var stages = rawStages
            .Where(stage => !string.IsNullOrWhiteSpace(stage))
            .Select(stage => stage.Trim())
            .ToArray();

        if (stages.Length == 1 && !StageNames.Contains(stages[0]) && string.IsNullOrWhiteSpace(project))
        {
            project = stages[0];
            Console.WriteLine($"Deprecated syntax detected. Use 'ai-memory index --project {project}' instead.");
            stages = [];
        }

        var invalidStages = stages.Where(stage => !StageNames.Contains(stage)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (invalidStages.Length > 0)
        {
            Console.WriteLine($"Invalid index stage(s): {string.Join(", ", invalidStages)}");
            Console.WriteLine("Valid stages: chunks, rules, knowledge");
            return null;
        }

        IReadOnlyList<IndexStage> resolved = stages.Length == 0
            ? [IndexStage.Chunks, IndexStage.Rules, IndexStage.Knowledge]
            : stages
                .Select(ParseStage)
                .Distinct()
                .ToArray();

        return new IndexPlan(resolved);
    }

    private static IndexStage ParseStage(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "chunks" => IndexStage.Chunks,
            "rules" => IndexStage.Rules,
            "knowledge" => IndexStage.Knowledge,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown index stage.")
        };
    }

    private readonly record struct IndexPlan(IReadOnlyList<IndexStage> Stages);

    private enum IndexStage
    {
        Chunks,
        Rules,
        Knowledge
    }

    private sealed class ProgressReporter : IDisposable
    {
        private readonly ProgressPanel _panel;

        public ProgressReporter(string label, int total)
        {
            _panel = new ProgressPanel(label, total, "no candidates", hasMutationCounts: true);
        }

        public void BeforeItem(string current) => _panel.BeforeItem(current);

        public void UpdateCurrent(string current) => _panel.UpdateCurrent(current);

        public void AfterItem(int inserted, int updated, int skipped) =>
            _panel.AfterItem(inserted, updated, skipped, indexedChunks: null);

        public void WriteMessage(string message) => _panel.WriteMessage(message);

        public void Complete(int inserted, int updated, int skipped) =>
            _panel.Complete(inserted, updated, skipped, indexedChunks: null);

        public void Dispose() => _panel.Dispose();
    }

    private sealed class ChunkIndexProgressReporter : IDisposable
    {
        private readonly ProgressPanel _panel;

        public ChunkIndexProgressReporter(string label, int total)
        {
            _panel = new ProgressPanel(label, total, "no files", hasMutationCounts: false);
        }

        public void BeforeItem(string current) => _panel.BeforeItem(current);

        public void AfterItem(int indexedChunks) =>
            _panel.AfterItem(inserted: 0, updated: 0, skipped: 0, indexedChunks);

        public void AfterChunk(int indexedChunks) => _panel.UpdateCounts(0, 0, 0, indexedChunks);

        public void Complete(int indexedChunks) =>
            _panel.Complete(inserted: 0, updated: 0, skipped: 0, indexedChunks);

        public void Dispose() => _panel.Dispose();
    }

    private sealed class ProgressPanel : IDisposable
    {
        private static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(1);
        private const int PanelLines = 4;

        private readonly object _gate = new();
        private readonly string _label;
        private readonly int _total;
        private readonly bool _hasMutationCounts;
        private readonly bool _interactive;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly EtaEstimator _eta = new();
        private readonly Timer? _timer;

        private TimeSpan _lastFallbackReport = TimeSpan.Zero;
        private int _processed;
        private int _inserted;
        private int _updated;
        private int _skipped;
        private int _indexedChunks;
        private bool _rendered;
        private bool _disposed;
        private string _current = "";

        public ProgressPanel(string label, int total, string emptyMessage, bool hasMutationCounts)
        {
            _label = label;
            _total = total;
            _hasMutationCounts = hasMutationCounts;
            _interactive = IsInteractiveConsole();

            if (total == 0)
            {
                Console.WriteLine($"  processing {label}: {emptyMessage}");
                return;
            }

            if (_interactive)
            {
                RenderLive();
                _timer = new Timer(_ => RenderFromTimer(), null, RenderInterval, RenderInterval);
            }
            else
            {
                WriteFallback(force: true);
            }
        }

        public void BeforeItem(string current)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _eta.StartItem(_stopwatch.Elapsed);
                _current = TruncateForProgress(current);
            }
        }

        public void UpdateCurrent(string current)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _current = TruncateForProgress(current);
            }
        }

        public void UpdateCounts(int inserted, int updated, int skipped, int? indexedChunks)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _inserted = inserted;
                _updated = updated;
                _skipped = skipped;
                if (indexedChunks is { } chunks)
                {
                    _indexedChunks = chunks;
                }

                WriteFallback(force: false);
            }
        }

        public void AfterItem(int inserted, int updated, int skipped, int? indexedChunks)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _eta.FinishItem(_stopwatch.Elapsed);
                _processed++;
                _inserted = inserted;
                _updated = updated;
                _skipped = skipped;
                if (indexedChunks is { } chunks)
                {
                    _indexedChunks = chunks;
                }

                WriteFallback(force: _processed >= _total);
            }
        }

        public void WriteMessage(string message)
        {
            lock (_gate)
            {
                if (_interactive && _rendered)
                {
                    ClearLivePanel();
                }

                Console.WriteLine(message);

                if (_interactive && !_disposed && _total > 0)
                {
                    RenderLive();
                }
            }
        }

        public void Complete(int inserted, int updated, int skipped, int? indexedChunks)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _inserted = inserted;
                _updated = updated;
                _skipped = skipped;
                if (indexedChunks is { } chunks)
                {
                    _indexedChunks = chunks;
                }

                if (_total > 0)
                {
                    if (_interactive)
                    {
                        RenderLive();
                    }
                    else
                    {
                        WriteFallback(force: true);
                    }
                }
            }

            Dispose();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _timer?.Dispose();
        }

        private void RenderFromTimer()
        {
            lock (_gate)
            {
                if (_disposed || !_interactive || _total == 0)
                {
                    return;
                }

                RenderLive();
            }
        }

        private void RenderLive()
        {
            if (_rendered)
            {
                Console.Write($"\u001b[{PanelLines}F");
            }

            foreach (var line in BuildLines())
            {
                Console.Write("\u001b[2K");
                Console.WriteLine(line);
            }

            _rendered = true;
        }

        private void ClearLivePanel()
        {
            Console.Write($"\u001b[{PanelLines}F");
            for (var i = 0; i < PanelLines; i++)
            {
                Console.Write("\u001b[2K");
                Console.WriteLine();
            }
            Console.Write($"\u001b[{PanelLines}F");
            _rendered = false;
        }

        private void WriteFallback(bool force)
        {
            if (_interactive)
            {
                return;
            }

            if (!force && _stopwatch.Elapsed - _lastFallbackReport < RenderInterval)
            {
                return;
            }

            _lastFallbackReport = _stopwatch.Elapsed;
            Console.WriteLine(BuildSingleLine());
        }

        private string[] BuildLines()
        {
            var progress = FormatProgress();
            var elapsed = EtaEstimator.FormatDuration(_stopwatch.Elapsed);
            var eta = _eta.Format(_processed, _total, _stopwatch.Elapsed);
            var rate = FormatRate();
            var countsHeader = _hasMutationCounts
                ? "Inserted   Updated    Skipped"
                : "Chunks";
            var counts = _hasMutationCounts
                ? $"{_inserted,8:N0}   {_updated,7:N0}   {_skipped,7:N0}"
                : $"{_indexedChunks,6:N0}";
            var width = GetConsoleWidth();

            return
            [
                Fit($"  {"Stage",-18} {"Progress",-19} {"Elapsed",-10} {"ETA",-14} {countsHeader}", width),
                Fit($"  {_label,-18} {progress,-19} {elapsed,-10} {eta,-14} {counts}", width),
                Fit($"  {"Current",-18} {TruncateForProgress(_current, Math.Max(20, width - 22))}", width),
                Fit($"  {"Rate",-18} {rate}", width)
            ];
        }

        private string BuildSingleLine()
        {
            var progress = FormatProgress();
            var elapsed = EtaEstimator.FormatDuration(_stopwatch.Elapsed);
            var eta = _eta.Format(_processed, _total, _stopwatch.Elapsed);
            var counts = _hasMutationCounts
                ? $"inserted {_inserted:N0}, updated {_updated:N0}, skipped {_skipped:N0}"
                : $"chunks {_indexedChunks:N0}";

            return $"  processing {_label}: {progress}, elapsed {elapsed}, {eta}, {counts}" +
                   $"{(string.IsNullOrWhiteSpace(_current) ? "" : $" | {_current}")}";
        }

        private string FormatProgress()
        {
            var percent = _total == 0 ? 100 : (int)Math.Round(_processed * 100d / _total);
            return $"{_processed:N0}/{_total:N0} ({percent}%)";
        }

        private string FormatRate()
        {
            if (_processed <= 0 || _stopwatch.Elapsed.TotalSeconds <= 0)
            {
                return "calculating";
            }

            var secondsPerItem = _stopwatch.Elapsed.TotalSeconds / _processed;
            var itemsPerMinute = 60d / secondsPerItem;
            return itemsPerMinute >= 1
                ? $"{itemsPerMinute:N1} items/min | avg {EtaEstimator.FormatDuration(TimeSpan.FromSeconds(secondsPerItem))}/item"
                : $"{(1d / itemsPerMinute):N1} min/item | avg {EtaEstimator.FormatDuration(TimeSpan.FromSeconds(secondsPerItem))}/item";
        }

        private static bool IsInteractiveConsole()
        {
            if (Console.IsOutputRedirected)
            {
                return false;
            }

            var term = Environment.GetEnvironmentVariable("TERM");
            return !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetConsoleWidth()
        {
            try
            {
                return Math.Clamp(Console.WindowWidth, 80, 180);
            }
            catch
            {
                return 120;
            }
        }

        private static string Fit(string value, int width)
        {
            value = Regex.Replace(value, @"\s+$", "");
            return value.Length <= width ? value : value[..Math.Max(0, width - 3)] + "...";
        }

        private static string TruncateForProgress(string value, int maxLength = 100)
        {
            value = Regex.Replace(value, @"\s+", " ").Trim();
            return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 3)] + "...";
        }
    }

    private sealed class EtaEstimator
    {
        private const int WarmupSamples = 5;
        private const double RecentWeight = 0.35d;
        private const double SmoothingAlpha = 0.08d;

        private TimeSpan? _itemStartedAt;
        private int _samples;
        private double _smoothedSecondsPerItem;

        public void StartItem(TimeSpan elapsed)
        {
            _itemStartedAt = elapsed;
        }

        public void FinishItem(TimeSpan elapsed)
        {
            if (_itemStartedAt is not { } startedAt)
            {
                return;
            }

            var seconds = Math.Max(0.001d, (elapsed - startedAt).TotalSeconds);
            _samples++;
            _smoothedSecondsPerItem = _samples == 1
                ? seconds
                : (_smoothedSecondsPerItem * (1d - SmoothingAlpha)) + (seconds * SmoothingAlpha);
            _itemStartedAt = null;
        }

        public string Format(int processed, int total, TimeSpan elapsed)
        {
            if (processed >= total)
            {
                return "done";
            }

            if (processed <= 0 || elapsed.TotalSeconds <= 0)
            {
                return "eta unknown";
            }

            var averageSecondsPerItem = elapsed.TotalSeconds / processed;
            var secondsPerItem = _samples < WarmupSamples
                ? averageSecondsPerItem
                : (averageSecondsPerItem * (1d - RecentWeight)) + (_smoothedSecondsPerItem * RecentWeight);

            var remainingSeconds = Math.Max(0d, (total - processed) * secondsPerItem);
            return $"eta ~{FormatDuration(TimeSpan.FromSeconds(remainingSeconds))}";
        }

        public static string FormatDuration(TimeSpan value)
        {
            if (value.TotalDays >= 1)
            {
                var roundedHours = (int)Math.Round(value.TotalHours);
                return $"{roundedHours / 24}d {roundedHours % 24}h";
            }

            if (value.TotalHours >= 2)
            {
                var roundedMinutes = RoundToNearest(value.TotalMinutes, 5);
                return $"{roundedMinutes / 60}h{roundedMinutes % 60:00}m";
            }

            if (value.TotalHours >= 1)
            {
                var roundedMinutes = RoundToNearest(value.TotalMinutes, 1);
                return $"{roundedMinutes / 60}h{roundedMinutes % 60:00}m";
            }

            if (value.TotalMinutes >= 10)
            {
                return $"{RoundToNearest(value.TotalMinutes, 1)}m";
            }

            if (value.TotalMinutes >= 1)
            {
                var roundedSeconds = RoundToNearest(value.TotalSeconds, 10);
                return $"{roundedSeconds / 60}m{roundedSeconds % 60:00}s";
            }

            return $"{Math.Max(1, (int)Math.Round(value.TotalSeconds))}s";
        }

        private static int RoundToNearest(double value, int step)
        {
            return Math.Max(step, (int)Math.Round(value / step) * step);
        }
    }
}
