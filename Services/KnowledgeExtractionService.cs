using System.Text.Json;
using System.Text.Json.Serialization;
using AiMemory.Models;

namespace AiMemory.Services;


public static class KnowledgeExtractionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IEnumerable<ExtractedKnowledge> ExtractKnowledge(ExtractionChunkResult chunk)
    {
        var content = chunk.Content;
        foreach (var (keyword, title, kind, confidence) in TextNormalizationService.KnowledgePatterns)
        {
            var index = content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var evidence = TextNormalizationService.GetEvidenceLine(content, index);
            var fullTitle = $"{chunk.Project}: {title}";
            var body = kind switch
            {
                "integration" => $"O projeto {chunk.Project} possui indício de integração ou comunicação externa relacionada a {keyword}.",
                "pattern" => $"O projeto {chunk.Project} possui indício de uso do padrão ou biblioteca {keyword}.",
                "technical_risk" => $"O projeto {chunk.Project} contém marcador técnico que precisa de revisão: {keyword}.",
                _ => $"O projeto {chunk.Project} contém evidência técnica relacionada a {keyword}."
            };
            var contentHash = HashService.Sha256($"{chunk.Project}|knowledge|{kind}|{TextNormalizationService.NormalizeKey(title)}|{chunk.File}");
            yield return new ExtractedKnowledge(chunk.Id, kind, fullTitle, body, chunk.File, chunk.Symbol, TextNormalizationService.Truncate(evidence, 500), confidence, contentHash);
        }
    }

    public static async Task<IReadOnlyList<ExtractedKnowledge>> ExtractSemanticKnowledgeAsync(
        OllamaService semanticService,
        ExtractionChunkResult chunk)
    {
        var json = await semanticService.GenerateJsonAsync(BuildSemanticKnowledgePrompt(chunk));
        var payload = JsonSerializer.Deserialize<SemanticKnowledgeResponse>(json, JsonOptions);
        if (payload?.Knowledge is null || payload.Knowledge.Count == 0)
        {
            return [];
        }

        var records = new List<ExtractedKnowledge>();
        foreach (var item in payload.Knowledge)
        {
            var kind = TextNormalizationService.NormalizeKnowledgeKind(item.Kind);
            var title = TextNormalizationService.NormalizeSentence(item.Title ?? "");
            var content = TextNormalizationService.NormalizeSentence(item.Content ?? "");
            var evidence = TextNormalizationService.NormalizeEvidence(item.Evidence ?? "");
            if (title.Length < 8 || content.Length < 12 || !TextNormalizationService.EvidenceExists(chunk.Content, evidence))
            {
                continue;
            }

            var fullTitle = title.StartsWith(chunk.Project + ":", StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{chunk.Project}: {title}";
            var confidence = TextNormalizationService.NormalizeConfidence(item.Confidence, 0.70m);
            var contentHash = HashService.Sha256($"{chunk.Project}|knowledge|{kind}|{TextNormalizationService.NormalizeKey(title)}|{chunk.File}");
            records.Add(new ExtractedKnowledge(
                chunk.Id,
                kind,
                TextNormalizationService.ToTitle(fullTitle, 140),
                content.EndsWith('.') ? content : content + ".",
                chunk.File,
                chunk.Symbol,
                TextNormalizationService.Truncate(evidence, 500),
                confidence,
                contentHash));
        }

        return records;
    }

    public static IEnumerable<ExtractedKnowledge> DeduplicateKnowledge(IEnumerable<ExtractedKnowledge> records)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records.OrderByDescending(r => r.Confidence))
        {
            if (seen.Add($"{TextNormalizationService.NormalizeKey(record.Kind)}|{TextNormalizationService.NormalizeKey(record.Title)}"))
            {
                yield return record;
            }
        }
    }

    private static string BuildSemanticKnowledgePrompt(ExtractionChunkResult chunk)
    {
        return string.Join(Environment.NewLine, [
            "You extract technical engineering knowledge from source code and documentation.",
            "Return JSON only. Use this exact shape:",
            """{"knowledge":[{"kind":"integration|pattern|technical_risk|architecture|configuration","title":"short technical fact","content":"technical meaning in Portuguese","evidence":"exact excerpt copied from the chunk","confidence":0.0}]}""",
            "Include integrations, frameworks, architectural patterns, configuration, infrastructure, persistence, messaging, authentication and explicit technical risks.",
            "Do not include business rules here.",
            "Every evidence value must be copied exactly from the chunk. If there is no exact evidence, return {\"knowledge\":[]}.",
            "",
            $"Project: {chunk.Project}",
            $"File: {chunk.File}",
            $"Language: {chunk.Language}",
            $"Chunk type: {chunk.ChunkType}",
            $"Symbol: {chunk.Symbol}",
            "",
            "Chunk:",
            chunk.Content
        ]);
    }

    private sealed class SemanticKnowledgeResponse
    {
        [JsonPropertyName("knowledge")]
        public List<SemanticKnowledgeItem> Knowledge { get; set; } = [];
    }

    private sealed class SemanticKnowledgeItem
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("evidence")]
        public string? Evidence { get; set; }

        [JsonPropertyName("confidence")]
        public decimal? Confidence { get; set; }
    }
}
