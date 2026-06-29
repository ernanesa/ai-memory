using AiMemory.Models;
using System.Collections.Generic;

namespace AiMemory.Services;

public static class ContextualChunkingService
{
    public static string GetContextualContent(CodeChunk chunk)
    {
        var parts = new List<string>
        {
            $"Project: {chunk.ProjectName}",
            $"File: {chunk.FilePath}"
        };

        if (chunk.Language == "csharp")
        {
            parts.Add("Lang: C#");
            if (!string.IsNullOrEmpty(chunk.SymbolName))
            {
                parts.Add($"Symbol: {chunk.SymbolName}");
            }
            parts.Add($"ChunkType: {chunk.ChunkType}");
        }
        else if (chunk.Language == "sql")
        {
            parts.Add("Lang: SQL");
            if (!string.IsNullOrEmpty(chunk.SymbolName))
            {
                parts.Add($"Symbol: {chunk.SymbolName}");
            }
        }
        else if (chunk.Language == "markdown")
        {
            parts.Add("Lang: Markdown");
            if (!string.IsNullOrEmpty(chunk.SymbolName))
            {
                parts.Add($"Section: {chunk.SymbolName}");
            }
        }
        else if (!string.IsNullOrEmpty(chunk.Language))
        {
            parts.Add($"Lang: {chunk.Language}");
        }

        return $"[CONTEXT: {string.Join(" | ", parts)}]\n{chunk.Content}";
    }
}
