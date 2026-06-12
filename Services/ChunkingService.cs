using System.Text.RegularExpressions;
using AiMemory.Models;

namespace AiMemory.Services;

public sealed class ChunkingService
{
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
            yield return new CodeChunk(projectName, root, relative, language, type, symbol, normalized, HashService.Sha256(normalized));
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
        const int max = 6_000;
        for (var i = 0; i < text.Length; i += max)
            yield return (type, symbol, text.Substring(i, Math.Min(max, text.Length - i)));
    }
}
