using AiMemory.Services;
using AiMemory.Configuration;
using System.Diagnostics;
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
            await IndexRulesAsync(projects, workspace.Name, config, db, ollama, model, candidateLimit);
        }

        if (plan.Value.Stages.Contains(IndexStage.Knowledge))
        {
            await IndexKnowledgeAsync(projects, workspace.Name, config, db, ollama, model, candidateLimit);
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

        foreach (var configuredProject in projects)
        {
            var root = ConfigService.ExpandPath(configuredProject.Path);
            if (!Directory.Exists(root))
            {
                Console.WriteLine($"Skipping {configuredProject.Name}: directory does not exist: {root}");
                continue;
            }

            Console.WriteLine($"Indexing chunks for {workspaceName}/{configuredProject.Name}: {root}");
            foreach (var file in chunker.EnumerateFiles(root))
            {
                foreach (var chunk in chunker.ChunkFile(configuredProject.Name, root, file))
                {
                    try
                    {
                        var embedding = await ollamaService.EmbedAsync(chunk.Content);
                        await pg.UpsertChunkAsync(workspaceName, chunk, embedding);
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
                Console.WriteLine($"  indexed {Path.GetRelativePath(root, file)}");
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
        int? candidateLimit)
    {
        Console.WriteLine($"Indexing rules for workspace '{workspaceName}' ({projects.Count} project(s)).");
        await using var pg = new PgVectorService(ConfigService.ResolveConnectionString(config, db));
        var ollamaService = new OllamaService(
            ConfigService.ResolveOllamaBaseUrl(config, ollama),
            ConfigService.ResolveEmbeddingModel(config, model));

        var projectNames = projects.Select(p => p.Name).ToArray();
        var stats = await pg.GetRuleExtractionStatsAsync(workspaceName, projectNames);
        var chunks = await pg.GetChunksForRuleExtractionAsync(workspaceName, projectNames, candidateLimit);
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var extracted = 0;

        PrintCandidateScope(stats, chunks.Count, candidateLimit);

        var progress = new ProgressReporter("rule chunks", chunks.Count);
        foreach (var chunk in chunks)
        {
            var chunkFailed = false;
            var chunkError = "";
            var candidates = ExtractBusinessRules(chunk).ToList();
            extracted += candidates.Count;

            if (candidates.Count == 0)
            {
                await pg.MarkExtractionChunkProcessedAsync(chunk.Id, "rules", chunk.ContentHash);
                progress.BeforeItem($"{chunk.File}{(chunk.Symbol is null ? "" : $"::{chunk.Symbol}")}");
                progress.AfterItem(inserted, updated, skipped);
                continue;
            }

            try
            {
                foreach (var candidate in candidates)
                {
                    progress.BeforeItem($"{candidate.SourceFile}{(candidate.SymbolName is null ? "" : $"::{candidate.SymbolName}")}");
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
                Console.WriteLine($"  failed rule chunk {chunk.File}: {ex.Message}");
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
        int? candidateLimit)
    {
        Console.WriteLine($"Indexing knowledge for workspace '{workspaceName}' ({projects.Count} project(s)).");
        await using var pg = new PgVectorService(ConfigService.ResolveConnectionString(config, db));
        var ollamaService = new OllamaService(
            ConfigService.ResolveOllamaBaseUrl(config, ollama),
            ConfigService.ResolveEmbeddingModel(config, model));

        var projectNames = projects.Select(p => p.Name).ToArray();
        var stats = await pg.GetKnowledgeExtractionStatsAsync(workspaceName, projectNames);
        var chunks = await pg.GetChunksForKnowledgeExtractionAsync(workspaceName, projectNames, candidateLimit);
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var extracted = 0;

        PrintCandidateScope(stats, chunks.Count, candidateLimit);

        var progress = new ProgressReporter("knowledge chunks", chunks.Count);
        foreach (var chunk in chunks)
        {
            var chunkFailed = false;
            var chunkError = "";
            var candidates = ExtractKnowledge(chunk).ToList();
            extracted += candidates.Count;

            if (candidates.Count == 0)
            {
                await pg.MarkExtractionChunkProcessedAsync(chunk.Id, "knowledge", chunk.ContentHash);
                progress.BeforeItem($"{chunk.File}{(chunk.Symbol is null ? "" : $"::{chunk.Symbol}")}");
                progress.AfterItem(inserted, updated, skipped);
                continue;
            }

            try
            {
                foreach (var candidate in candidates)
                {
                    progress.BeforeItem($"{candidate.Source}{(candidate.SymbolName is null ? "" : $"::{candidate.SymbolName}")}");
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
                Console.WriteLine($"  failed knowledge chunk {chunk.File}: {ex.Message}");
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

    private static IEnumerable<ExtractedBusinessRule> ExtractBusinessRules(ExtractionChunkResult chunk)
    {
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in BusinessExceptionRegex.Matches(chunk.Content))
        {
            var message = NormalizeSentence(match.Groups[1].Value);
            if (!LooksLikeBusinessRule(message) || !emitted.Add(message))
            {
                continue;
            }

            yield return CreateBusinessRule(chunk, message, GetEvidenceLine(chunk.Content, match.Index), 0.88m);
        }

        foreach (Match match in ValidationMessageRegex.Matches(chunk.Content))
        {
            var message = NormalizeSentence(match.Groups[1].Value);
            if (!LooksLikeBusinessRule(message) || !emitted.Add(message))
            {
                continue;
            }

            yield return CreateBusinessRule(chunk, message, GetEvidenceLine(chunk.Content, match.Index), 0.78m);
        }

        foreach (Match match in ErrorContextMessageRegex.Matches(chunk.Content))
        {
            var message = NormalizeSentence(match.Groups[1].Value);
            if (!LooksLikeBusinessRule(message) || !emitted.Add(message))
            {
                continue;
            }

            yield return CreateBusinessRule(chunk, message, GetEvidenceLine(chunk.Content, match.Index), 0.84m);
        }

        foreach (var line in GetRelevantLines(chunk.Content))
        {
            var sentence = NormalizeSentence(line);
            if (!LooksLikeBusinessRule(sentence) || sentence.Length > 220 || !emitted.Add(sentence))
            {
                continue;
            }

            yield return CreateBusinessRule(chunk, sentence, line, 0.64m);
        }
    }

    private static ExtractedBusinessRule CreateBusinessRule(ExtractionChunkResult chunk, string message, string evidence, decimal confidence)
    {
        var title = ToTitle(message, 90);
        var description = message.EndsWith('.') ? message : message + ".";
        var contentHash = HashService.Sha256($"{chunk.Project}|rule|{NormalizeKey(title)}");
        return new ExtractedBusinessRule(chunk.Id, title, description, chunk.File, chunk.Symbol, Truncate(evidence, 500), confidence, contentHash);
    }

    private static IEnumerable<ExtractedKnowledge> ExtractKnowledge(ExtractionChunkResult chunk)
    {
        var content = chunk.Content;
        foreach (var (keyword, title, kind, confidence) in KnowledgePatterns)
        {
            var index = content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var evidence = GetEvidenceLine(content, index);
            var fullTitle = $"{chunk.Project}: {title}";
            var body = kind switch
            {
                "integration" => $"O projeto {chunk.Project} possui indício de integração ou comunicação externa relacionada a {keyword}.",
                "pattern" => $"O projeto {chunk.Project} possui indício de uso do padrão ou biblioteca {keyword}.",
                "technical_risk" => $"O projeto {chunk.Project} contém marcador técnico que precisa de revisão: {keyword}.",
                _ => $"O projeto {chunk.Project} contém evidência técnica relacionada a {keyword}."
            };
            var contentHash = HashService.Sha256($"{chunk.Project}|knowledge|{kind}|{NormalizeKey(title)}|{chunk.File}");
            yield return new ExtractedKnowledge(chunk.Id, kind, fullTitle, body, chunk.File, chunk.Symbol, Truncate(evidence, 500), confidence, contentHash);
        }
    }

    private static IEnumerable<string> GetRelevantLines(string content)
    {
        return content
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length is >= 18 and <= 260)
            .Where(line =>
                line.Contains("não pode", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("nao pode", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("deve", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("obrigatório", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("obrigatorio", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("inválido", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("invalido", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ErroContext", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ErrosContext", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("AdicionarErro", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("AdicionaErro", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("AddErro", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("AddError", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("AddFailure", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("AddNotification", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Notificar", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("RuleFor", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Elegivel", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Elegível", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Permite", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Bloqueado", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Cancelado", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Vencido", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetEvidenceLine(string content, int index)
    {
        var start = content.LastIndexOf('\n', Math.Clamp(index, 0, Math.Max(0, content.Length - 1)));
        start = start < 0 ? 0 : start + 1;
        var end = content.IndexOf('\n', index);
        end = end < 0 ? content.Length : end;
        return content[start..end].Trim();
    }

    private static bool LooksLikeBusinessRule(string value)
    {
        if (value.Length < 12)
        {
            return false;
        }

        return value.Contains("não pode", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("nao pode", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("deve", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("obrigatório", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("obrigatorio", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("inválido", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("invalido", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("bloquead", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("cancelad", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("vencid", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("ErroContext", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("ErrosContext", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("AdicionarErro", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("AdicionaErro", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("AddErro", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("AddError", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("AddFailure", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("AddNotification", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Notificar", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("RuleFor", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Elegivel", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Elegível", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Permite", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSentence(string value)
    {
        return Regex.Replace(value, @"\s+", " ").Trim().Trim('"', '\'', '.', ';', ',');
    }

    private static string NormalizeKey(string value)
    {
        return Regex.Replace(value.ToLowerInvariant(), @"\s+", " ").Trim();
    }

    private static string ToTitle(string value, int maxLength)
    {
        var normalized = NormalizeSentence(value);
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "...";
    }

    private static string Truncate(string value, int maxLength)
    {
        var normalized = NormalizeSentence(value);
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "...";
    }

    private static void PrintCandidateScope(ExtractionStats stats, int selectedCandidateChunks, int? candidateLimit)
    {
        Console.WriteLine($"  total chunks in scope: {stats.TotalChunks:N0}");
        Console.WriteLine($"  matching candidate chunks before limit: {stats.CandidateChunks:N0}");
        Console.WriteLine($"  pending candidate chunks: {stats.PendingCandidateChunks:N0}");
        Console.WriteLine(candidateLimit is null
            ? "  candidate limit: none"
            : $"  candidate limit: {candidateLimit.Value:N0}");
        Console.WriteLine($"  candidate chunks selected: {selectedCandidateChunks:N0}");
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

    private sealed class ProgressReporter
    {
        private const int ItemInterval = 25;
        private static readonly TimeSpan TimeInterval = TimeSpan.FromSeconds(5);

        private readonly string _label;
        private readonly int _total;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private TimeSpan _lastReport = TimeSpan.Zero;
        private int _processed;
        private string _current = "";

        public ProgressReporter(string label, int total)
        {
            _label = label;
            _total = total;
            if (total == 0)
            {
                Console.WriteLine($"  processing {label}: no candidates");
            }
            else
            {
                Console.WriteLine($"  processing {label}: 0/{total:N0} (0%)");
            }
        }

        public void BeforeItem(string current)
        {
            _current = TruncateForProgress(current);
        }

        public void AfterItem(int inserted, int updated, int skipped)
        {
            _processed++;
            if (_processed == _total ||
                _processed % ItemInterval == 0 ||
                _stopwatch.Elapsed - _lastReport >= TimeInterval)
            {
                Report(inserted, updated, skipped);
            }
        }

        public void Complete(int inserted, int updated, int skipped)
        {
            if (_total == 0)
            {
                return;
            }

            if (_processed != _total)
            {
                Report(inserted, updated, skipped);
            }
        }

        private void Report(int inserted, int updated, int skipped)
        {
            _lastReport = _stopwatch.Elapsed;
            var percent = _total == 0 ? 100 : (int)Math.Round(_processed * 100d / _total);
            var rate = _stopwatch.Elapsed.TotalSeconds <= 0 ? 0 : _processed / _stopwatch.Elapsed.TotalSeconds;
            var remaining = rate <= 0 || _processed >= _total
                ? "done"
                : $"eta {TimeSpan.FromSeconds((_total - _processed) / rate):hh\\:mm\\:ss}";

            Console.WriteLine(
                $"  processing {_label}: {_processed:N0}/{_total:N0} ({percent}%) " +
                $"inserted {inserted:N0}, updated {updated:N0}, skipped {skipped:N0}, {remaining}" +
                $"{(string.IsNullOrWhiteSpace(_current) ? "" : $" | {_current}")}");
        }

        private static string TruncateForProgress(string value)
        {
            value = Regex.Replace(value, @"\s+", " ").Trim();
            return value.Length <= 100 ? value : value[..97] + "...";
        }
    }

    private static readonly Regex BusinessExceptionRegex = new(
        @"throw\s+new\s+[A-Za-z0-9_.]*(?:Business|Domain|Validation)?Exception\s*\(\s*""([^""]{12,240})""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ValidationMessageRegex = new(
        @"(?:WithMessage|AddFailure|AddNotification)\s*\(\s*""([^""]{12,240})""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ErrorContextMessageRegex = new(
        @"(?:ErroContext|ErrosContext|AdicionarErro|AdicionaErro|AddErro|AddError|AddFailure|AddNotification|Notificar|AddNotification)\s*(?:<[^>]+>)?\s*\([^\)]*?""([^""]{8,240})""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly (string Keyword, string Title, string Kind, decimal Confidence)[] KnowledgePatterns =
    [
        ("HttpClient", "usa HttpClient para comunicação externa", "integration", 0.72m),
        ("MassTransit", "usa MassTransit para mensageria", "integration", 0.82m),
        ("RabbitMQ", "usa RabbitMQ para mensageria", "integration", 0.82m),
        ("Kafka", "usa Kafka para mensageria", "integration", 0.82m),
        ("MediatR", "usa MediatR para handlers e dispatch interno", "pattern", 0.78m),
        ("EntityFramework", "usa Entity Framework para persistência", "pattern", 0.78m),
        ("TODO", "possui marcador TODO", "technical_risk", 0.62m),
        ("FIXME", "possui marcador FIXME", "technical_risk", 0.72m),
        ("HACK", "possui marcador HACK", "technical_risk", 0.72m)
    ];
}
