using System.Text.RegularExpressions;

namespace AiMemory.Services
{
    public static class TextNormalizationService
    {
        public static readonly Regex BusinessExceptionRegex = new(
            @"throw\s+new\s+[A-Za-z0-9_.]*(?:Business|Domain|Validation)?Exception\s*\(\s*""([^""]{12,240})""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static readonly Regex ValidationMessageRegex = new(
            @"(?:WithMessage|AddFailure|AddNotification)\s*\(\s*""([^""]{12,240})""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static readonly Regex ErrorContextMessageRegex = new(
            @"(?:ErroContext|ErrosContext|AdicionarErro|AdicionaErro|AddErro|AddError|AddFailure|AddNotification|Notificar|AddNotification)\s*(?:<[^>]+>)?\s*\([^\)]*?""([^""]{8,240})""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        public static readonly (string Keyword, string Title, string Kind, decimal Confidence)[] KnowledgePatterns =
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

        public static string NormalizeSentence(string value)
        {
            return Regex.Replace(value, @"\s+", " ").Trim().Trim('"', '\'', '.', ';', ',');
        }

        public static string NormalizeEvidence(string value)
        {
            return Regex.Replace(value, @"\s+", " ").Trim().Trim('"', '\'');
        }

        public static string NormalizeKey(string value)
        {
            return Regex.Replace(value.ToLowerInvariant(), @"\s+", " ").Trim();
        }

        public static bool EvidenceExists(string content, string evidence)
        {
            if (evidence.Length < 8)
            {
                return false;
            }

            return content.Contains(evidence, StringComparison.OrdinalIgnoreCase) ||
                   content.Contains(NormalizeSentence(evidence), StringComparison.OrdinalIgnoreCase);
        }

        public static decimal NormalizeConfidence(decimal? value, decimal fallback)
        {
            var confidence = value is > 0 ? value.Value : fallback;
            return Math.Clamp(confidence, 0.10m, 0.95m);
        }

        public static string NormalizeKnowledgeKind(string? value)
        {
            var normalized = NormalizeKey(value ?? "");
            return normalized switch
            {
                "integration" => "integration",
                "pattern" => "pattern",
                "technical_risk" => "technical_risk",
                "architecture" => "architecture",
                "configuration" => "configuration",
                _ => "technical"
            };
        }

        public static string ToTitle(string value, int maxLength)
        {
            var normalized = NormalizeSentence(value);
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "...";
        }

        public static string Truncate(string value, int maxLength)
        {
            var normalized = NormalizeSentence(value);
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "...";
        }

        public static string GetEvidenceLine(string content, int index)
        {
            var start = content.LastIndexOf('\n', Math.Clamp(index, 0, Math.Max(0, content.Length - 1)));
            start = start < 0 ? 0 : start + 1;
            var end = content.IndexOf('\n', index);
            end = end < 0 ? content.Length : end;
            return content[start..end].Trim();
        }

        public static bool LooksLikeBusinessRule(string value)
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

        public static bool LooksLikeSemanticBusinessRule(string title, string description, string evidence)
        {
            var combined = NormalizeKey($"{title} {description} {evidence}");
            if (LooksLikeTechnicalFact(combined))
            {
                return false;
            }

            return combined.Contains("nao pode", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("não pode", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("deve", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("obrigator", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("inválid", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("invalido", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("bloque", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("venc", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("eleg", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("permitid", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("proibid", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("restri", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("valid", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("status", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("regra", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("permiss", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("limite", StringComparison.OrdinalIgnoreCase);
        }

        public static bool LooksLikeTechnicalFact(string normalized)
        {
            return normalized.Contains("codigo corretor", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("código corretor", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("codigo representante", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("código representante", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("identificador unico", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("identificador único", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("const string", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("public const", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("permite obter", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("permite buscar", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("permite listar", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("obtencao de", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("obtenção de", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("busca de", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("consulta de", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("retorna ", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("criação de lista", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("criacao de lista", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("cria uma lista", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("ienumerable", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("task<ienumerable", StringComparison.OrdinalIgnoreCase);
        }

        public static IEnumerable<string> GetRelevantLines(string content)
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
    }
}
