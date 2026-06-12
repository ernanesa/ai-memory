using System.Text.RegularExpressions;
using AiMemory.Models;

namespace AiMemory.Services;

public sealed class ChunkingService
{
    private const int MaxChunkLength = 1_000;

    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    { ".git", "bin", "obj", "node_modules", "dist", "coverage", "packages", ".idea", ".vs" };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".csproj", ".sln", ".sql", ".json", ".md", ".yml", ".yaml", ".config", ".props", ".targets" };

    public IEnumerable<string> EnumerateFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => IgnoredDirs.Contains(part)))
            .Where(path => AllowedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => new FileInfo(path).Length < 512_000);
    }

    public IEnumerable<CodeChunk> ChunkFile(string projectName, string root, string filePath)
    {
        var text = File.ReadAllText(filePath);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var relative = Path.GetRelativePath(root, filePath);
        var language = ext switch
        {
            ".cs" => "csharp",
            ".sql" => "sql",
            ".md" => "markdown",
            ".json" => "json",
            _ => ext.TrimStart('.')
        };

        foreach (var (type, symbol, content) in ext switch
        {
            ".cs" => ChunkCSharp(text),
            ".sql" => ChunkSql(text),
            ".md" => ChunkMarkdown(text),
            _ => ChunkBySize(text, "file", null)
        })
        {
            var normalized = content.Trim();
            if (normalized.Length < 40) continue;
            foreach (var part in SplitChunk(normalized))
                yield return new CodeChunk(projectName, root, relative, language, type, symbol, part, HashService.Sha256(part));
        }
    }

    private static IEnumerable<(string Type, string? Symbol, string Content)> ChunkCSharp(string text)
    {
        // MVP: split around classes and methods using lightweight regex.
        // Future improvement: replace with Roslyn syntax tree for exact semantic chunks.
        var matches = Regex.Matches(text, @"(?m)^\s*(public|private|protected|internal|sealed|static|abstract|async|partial|\s)+\s+(class|interface|record|enum|struct|Task<[^>]+>|Task|void|[A-Za-z0-9_<>,]+)\s+([A-Za-z0-9_]+)\s*(\(|:|\{)");
        if (matches.Count == 0)
        {
            foreach (var c in ChunkBySize(text, "file", null)) yield return c;
            yield break;
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var content = text[start..end];
            var symbol = matches[i].Groups[3].Value;
            var kind = matches[i].Value.Contains(" class ") || matches[i].Value.Contains(" record ") ? "type" : "member";
            if (content.Length > 8_000)
            {
                foreach (var c in ChunkBySize(content, kind, symbol)) yield return c;
            }
            else yield return (kind, symbol, content);
        }
    }

    private static IEnumerable<(string Type, string? Symbol, string Content)> ChunkSql(string text)
    {
        var parts = Regex.Split(text, @"(?im)^\s*GO\s*$").Where(p => !string.IsNullOrWhiteSpace(p));
        foreach (var part in parts)
        {
            var m = Regex.Match(part, @"(?im)\b(create|alter)\s+(procedure|proc|view|function|trigger)\s+([\[\]\w\.]+)");
            yield return (m.Success ? m.Groups[2].Value.ToLowerInvariant() : "sql", m.Success ? m.Groups[3].Value : null, part);
        }
    }

    private static IEnumerable<(string Type, string? Symbol, string Content)> ChunkMarkdown(string text)
    {
        var matches = Regex.Matches(text, @"(?m)^#{1,3}\s+(.+)$");
        if (matches.Count == 0)
        {
            foreach (var c in ChunkBySize(text, "markdown", null)) yield return c;
            yield break;
        }
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            yield return ("section", matches[i].Groups[1].Value.Trim(), text[start..end]);
        }
    }

    private static IEnumerable<(string Type, string? Symbol, string Content)> ChunkBySize(string text, string type, string? symbol)
    {
        foreach (var part in SplitChunk(text))
            yield return (type, symbol, part);
    }

    private static IEnumerable<string> SplitChunk(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(MaxChunkLength, text.Length - start);
            if (start + length < text.Length)
            {
                var newline = text.LastIndexOf('\n', start + length - 1, length);
                if (newline > start + 200)
                    length = newline - start + 1;
            }

            var part = text.Substring(start, length).Trim();
            if (part.Length >= 40) yield return part;
            start += length;
        }
    }
}
