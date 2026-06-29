using System;
using System.Collections.Generic;
using System.Linq;

namespace AiMemory.Services;

public static class RerankerService
{
    public static IReadOnlyList<CodeSearchResult> RerankCode(IEnumerable<CodeSearchResult> results, string query)
    {
        var queryTerms = query.Split(new[] { ' ', '.', '_', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 2)
            .ToArray();

        return results
            .Select(r =>
            {
                double score = r.Distance; // No RRF, quanto maior o score, melhor

                // 1. Boost para correspondência exata do nome do símbolo
                if (!string.IsNullOrEmpty(r.Symbol))
                {
                    var symbolLower = r.Symbol.ToLowerInvariant();
                    if (queryTerms.Any(term => symbolLower.Contains(term)))
                    {
                        score += 0.25; // boost significativo de relevância
                    }

                    if (queryTerms.Length > 0 && query.Contains(r.Symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 0.15;
                    }
                }

                // 2. Penalidade para arquivos de configuração e documentação se a query parecer buscar código estrutural
                var isConfig = r.Language is "json" or "yaml" or "yml" or "config" or "xml";
                var isDoc = r.Language is "markdown" or "md" or "text" or "txt";
                var queryLooksStructural = query.Contains("class", StringComparison.OrdinalIgnoreCase) ||
                                           query.Contains("public", StringComparison.OrdinalIgnoreCase) ||
                                           query.Contains("void", StringComparison.OrdinalIgnoreCase) ||
                                           query.Contains("async", StringComparison.OrdinalIgnoreCase) ||
                                           query.Contains("interface", StringComparison.OrdinalIgnoreCase);

                if (queryLooksStructural)
                {
                    if (isConfig)
                    {
                        score -= 0.15;
                    }
                    else if (isDoc)
                    {
                        score -= 0.10;
                    }
                }

                return r with { Distance = score };
            })
            .OrderByDescending(r => r.Distance) // Ordena pelo novo score (RRF + heuristic boost)
            .ToList();
    }
}
