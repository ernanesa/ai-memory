using System.Text.RegularExpressions;
using AiMemory.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        var chunks = TryChunkCSharpWithRoslyn(text);
        return chunks.Count > 0 ? chunks : ChunkCSharpWithRegex(text);
    }

    private static IReadOnlyList<(string Type, string? Symbol, string Content)> TryChunkCSharpWithRoslyn(string text)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetCompilationUnitRoot();
            var chunks = new List<(string Type, string? Symbol, string Content)>();
            var topLevelTypes = root
                .DescendantNodes(descendIntoChildren: node => node is not BaseTypeDeclarationSyntax)
                .OfType<BaseTypeDeclarationSyntax>()
                .ToList();

            foreach (var type in topLevelTypes)
            {
                var symbol = GetTypeSymbolName(type);
                var content = GetNodeText(text, type);

                if (content.Length <= 8_000)
                {
                    chunks.Add(("type", symbol, content));
                    continue;
                }

                if (type is not TypeDeclarationSyntax typeDeclaration || typeDeclaration.Members.Count == 0)
                {
                    chunks.Add(("type", symbol, content));
                    continue;
                }

                foreach (var member in typeDeclaration.Members)
                {
                    var memberSymbol = GetMemberSymbolName(typeDeclaration, member);
                    var memberContent = BuildMemberChunk(text, root, typeDeclaration, member);
                    chunks.Add((member is BaseTypeDeclarationSyntax ? "type" : "member", memberSymbol, memberContent));
                }
            }

            return chunks;
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<(string Type, string? Symbol, string Content)> ChunkCSharpWithRegex(string text)
    {
        // Fallback: split around classes and methods using lightweight regex.
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

    private static string BuildMemberChunk(
        string source,
        CompilationUnitSyntax root,
        TypeDeclarationSyntax type,
        MemberDeclarationSyntax member)
    {
        var context = new List<string>();
        var usings = root.Usings.Select(u => u.ToFullString().Trim()).Where(u => u.Length > 0).ToList();
        if (usings.Count > 0) context.Add(string.Join(Environment.NewLine, usings));

        var namespaceName = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        if (!string.IsNullOrWhiteSpace(namespaceName)) context.Add($"namespace {namespaceName};");

        context.Add(GetTypeDeclarationPrefix(source, type));
        context.Add(GetNodeText(source, member));
        context.Add("}");

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", context);
    }

    private static string GetTypeDeclarationPrefix(string source, TypeDeclarationSyntax type)
    {
        var content = GetNodeText(source, type);
        var openBrace = content.IndexOf('{', StringComparison.Ordinal);
        if (openBrace >= 0)
            return content[..(openBrace + 1)].Trim();

        var firstLineEnd = content.IndexOfAny(['\r', '\n']);
        return firstLineEnd >= 0 ? content[..firstLineEnd].Trim() : content.Trim();
    }

    private static string GetNodeText(string source, CSharpSyntaxNode node)
    {
        var span = node.FullSpan;
        return source.Substring(span.Start, span.Length).Trim();
    }

    private static string? GetTypeSymbolName(BaseTypeDeclarationSyntax type)
    {
        var parts = new Stack<string>();
        parts.Push(type.Identifier.ValueText);

        foreach (var parentType in type.Ancestors().OfType<BaseTypeDeclarationSyntax>())
            parts.Push(parentType.Identifier.ValueText);

        var namespaceName = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        return string.IsNullOrWhiteSpace(namespaceName)
            ? string.Join(".", parts)
            : $"{namespaceName}.{string.Join(".", parts)}";
    }

    private static string? GetMemberSymbolName(TypeDeclarationSyntax containingType, MemberDeclarationSyntax member)
    {
        var typeName = GetTypeSymbolName(containingType);
        var memberName = member switch
        {
            MethodDeclarationSyntax method => $"{method.Identifier.ValueText}{method.ParameterList}",
            ConstructorDeclarationSyntax constructor => $"{constructor.Identifier.ValueText}{constructor.ParameterList}",
            DestructorDeclarationSyntax destructor => $"~{destructor.Identifier.ValueText}()",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            IndexerDeclarationSyntax indexer => $"this{indexer.ParameterList}",
            EventDeclarationSyntax eventDeclaration => eventDeclaration.Identifier.ValueText,
            EventFieldDeclarationSyntax eventField => string.Join(",", eventField.Declaration.Variables.Select(v => v.Identifier.ValueText)),
            FieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(v => v.Identifier.ValueText)),
            OperatorDeclarationSyntax operatorDeclaration => $"operator {operatorDeclaration.OperatorToken.ValueText}{operatorDeclaration.ParameterList}",
            ConversionOperatorDeclarationSyntax conversion => $"operator {conversion.Type}{conversion.ParameterList}",
            BaseTypeDeclarationSyntax nestedType => nestedType.Identifier.ValueText,
            _ => null
        };

        return string.IsNullOrWhiteSpace(memberName)
            ? typeName
            : $"{typeName}.{memberName}";
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
