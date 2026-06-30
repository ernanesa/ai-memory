using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AiMemory.Services
{

    public static class RuleExtractionService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static IEnumerable<ExtractedBusinessRule> ExtractBusinessRules(ExtractionChunkResult chunk)
        {
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in TextNormalizationService.BusinessExceptionRegex.Matches(chunk.Content))
            {
                var message = TextNormalizationService.NormalizeSentence(match.Groups[1].Value);
                if (!TextNormalizationService.LooksLikeBusinessRule(message) || !emitted.Add(message))
                {
                    continue;
                }

                yield return CreateBusinessRule(chunk, message, TextNormalizationService.GetEvidenceLine(chunk.Content, match.Index), 0.88m);
            }

            foreach (Match match in TextNormalizationService.ValidationMessageRegex.Matches(chunk.Content))
            {
                var message = TextNormalizationService.NormalizeSentence(match.Groups[1].Value);
                if (!TextNormalizationService.LooksLikeBusinessRule(message) || !emitted.Add(message))
                {
                    continue;
                }

                yield return CreateBusinessRule(chunk, message, TextNormalizationService.GetEvidenceLine(chunk.Content, match.Index), 0.78m);
            }

            foreach (Match match in TextNormalizationService.ErrorContextMessageRegex.Matches(chunk.Content))
            {
                var message = TextNormalizationService.NormalizeSentence(match.Groups[1].Value);
                if (!TextNormalizationService.LooksLikeBusinessRule(message) || !emitted.Add(message))
                {
                    continue;
                }

                yield return CreateBusinessRule(chunk, message, TextNormalizationService.GetEvidenceLine(chunk.Content, match.Index), 0.84m);
            }

            foreach (var line in TextNormalizationService.GetRelevantLines(chunk.Content))
            {
                var sentence = TextNormalizationService.NormalizeSentence(line);
                if (!TextNormalizationService.LooksLikeBusinessRule(sentence) || sentence.Length > 220 || !emitted.Add(sentence))
                {
                    continue;
                }

                yield return CreateBusinessRule(chunk, sentence, line, 0.64m);
            }
        }

        private static ExtractedBusinessRule CreateBusinessRule(ExtractionChunkResult chunk, string message, string evidence, decimal confidence)
        {
            var title = TextNormalizationService.ToTitle(message, 90);
            var description = message.EndsWith('.') ? message : message + ".";
            var contentHash = HashService.Sha256($"{chunk.Project}|rule|{TextNormalizationService.NormalizeKey(title)}");
            return new ExtractedBusinessRule(chunk.Id, title, description, chunk.File, chunk.Symbol, TextNormalizationService.Truncate(evidence, 500), confidence, contentHash);
        }

        public static async Task<IReadOnlyList<ExtractedBusinessRule>> ExtractSemanticBusinessRulesAsync(
            OllamaService semanticService,
            ExtractionChunkResult chunk)
        {
            var json = await semanticService.GenerateJsonAsync(BuildSemanticRulesPrompt(chunk));
            var payload = JsonSerializer.Deserialize<SemanticRulesResponse>(json, JsonOptions);
            if (payload?.Rules is null || payload.Rules.Count == 0)
            {
                return [];
            }

            var rules = new List<ExtractedBusinessRule>();
            foreach (var item in payload.Rules)
            {
                var title = TextNormalizationService.NormalizeSentence(item.Title ?? "");
                var description = TextNormalizationService.NormalizeSentence(item.Description ?? "");
                var evidence = TextNormalizationService.NormalizeEvidence(item.Evidence ?? "");
                if (title.Length < 8 ||
                    description.Length < 12 ||
                    !TextNormalizationService.EvidenceExists(chunk.Content, evidence) ||
                    !TextNormalizationService.LooksLikeSemanticBusinessRule(title, description, evidence))
                {
                    continue;
                }

                var confidence = TextNormalizationService.NormalizeConfidence(item.Confidence, 0.72m);
                var contentHash = HashService.Sha256($"{chunk.Project}|rule|{TextNormalizationService.NormalizeKey(title)}");
                rules.Add(new ExtractedBusinessRule(
                    chunk.Id,
                    TextNormalizationService.ToTitle(title, 90),
                    description.EndsWith('.') ? description : description + ".",
                    chunk.File,
                    chunk.Symbol,
                    TextNormalizationService.Truncate(evidence, 500),
                    confidence,
                    contentHash));
            }

            return rules;
        }

        public static IEnumerable<ExtractedBusinessRule> DeduplicateBusinessRules(IEnumerable<ExtractedBusinessRule> rules)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules.OrderByDescending(r => r.Confidence))
            {
                if (seen.Add(TextNormalizationService.NormalizeKey(rule.Title)))
                {
                    yield return rule;
                }
            }
        }

        private static string BuildSemanticRulesPrompt(ExtractionChunkResult chunk)
        {
            return string.Join(Environment.NewLine, [
                "You extract business rules from source code and documentation.",
                "Return JSON only. Use this exact shape:",
                """{"rules":[{"title":"short rule title","description":"business meaning in Portuguese","evidence":"exact excerpt copied from the chunk","confidence":0.0}]}""",
                "Only include real product/domain constraints, validations, permissions, state transitions, eligibility rules or required data.",
                "Do not include constants, GUIDs, method signatures, repository/query capabilities, DTO shapes, mappings, configuration, or technical implementation details as business rules.",
                "Reject facts phrased as 'permite obter', 'busca', 'retorna', 'cria lista', 'codigo constante' unless they impose a domain restriction, obligation or decision.",
                "Every evidence value must be copied exactly from the chunk. If there is no exact evidence, return {\"rules\":[]}.",
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

        private sealed class SemanticRulesResponse
        {
            [JsonPropertyName("rules")]
            public List<SemanticRuleItem> Rules { get; set; } = [];
        }

        private sealed class SemanticRuleItem
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }

            [JsonPropertyName("evidence")]
            public string? Evidence { get; set; }

            [JsonPropertyName("confidence")]
            public decimal? Confidence { get; set; }
        }
    }
}
