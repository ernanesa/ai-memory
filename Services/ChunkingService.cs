using System.Diagnostics;
using System.Text.RegularExpressions;
using AiMemory.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiMemory.Services
{
    public sealed class ChunkingService
    {
        private const int MaxChunkLength = 1_000;

        private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "bin", "obj", "node_modules", "dist", "coverage", "packages", ".idea", ".vs", ".vscode" };

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".csproj", ".sln", ".sql", ".json", ".md", ".yml", ".yaml", ".config", ".props", ".targets", ".razor", ".cshtml" };

        public IEnumerable<string> EnumerateFiles(string root)
        {
            var testProjectDirectories = FindTestProjectDirectories(root).ToArray();
            var files = EnumerateGitVisibleFiles(root) ?? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
            return files
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => IgnoredDirs.Contains(part)))
                .Where(path => !IsUnderAnyDirectory(path, testProjectDirectories))
                .Where(path => AllowedExtensions.Contains(Path.GetExtension(path)))
                .Where(path => new FileInfo(path).Length < 512_000)
                .Where(path => !ShouldSkipSourceFile(path));
        }

        public IEnumerable<CodeChunk> ChunkFile(string projectName, string root, string filePath)
        {
            var text = File.ReadAllText(filePath);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".cs" && (IsEntityFrameworkMigrationSource(text) || IsTestSource(filePath, text)))
            {
                yield break;
            }

            var relative = Path.GetRelativePath(root, filePath);
            var language = ext switch
            {
                ".cs" => "csharp",
                ".sql" => "sql",
                ".md" => "markdown",
                ".json" => "json",
                ".razor" => "razor",
                ".cshtml" => "cshtml",
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

        private static bool ShouldSkipSourceFile(string filePath)
        {
            if (!Path.GetExtension(filePath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var text = File.ReadAllText(filePath);
                return IsEntityFrameworkMigrationSource(text) || IsTestSource(filePath, text);
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> FindTestProjectDirectories(string root)
        {
            IEnumerable<string> projectFiles;
            try
            {
                projectFiles = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                    .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => IgnoredDirs.Contains(part)))
                    .ToArray();
            }
            catch
            {
                yield break;
            }

            foreach (var projectFile in projectFiles)
            {
                string text;
                try
                {
                    text = File.ReadAllText(projectFile);
                }
                catch
                {
                    continue;
                }

                if (IsTestProject(projectFile, text))
                {
                    yield return Path.GetDirectoryName(projectFile) ?? root;
                }
            }
        }

        private static bool IsUnderAnyDirectory(string filePath, IReadOnlyList<string> directories)
        {
            if (directories.Count == 0)
            {
                return false;
            }

            var fullPath = Path.GetFullPath(filePath);
            return directories.Any(directory =>
            {
                var fullDirectory = Path.GetFullPath(directory);
                var relative = Path.GetRelativePath(fullDirectory, fullPath);
                return relative == "." ||
                       (!relative.StartsWith("..", StringComparison.Ordinal) &&
                        !Path.IsPathRooted(relative));
            });
        }

        private static bool IsTestProject(string projectFile, string text)
        {
            var fileName = Path.GetFileNameWithoutExtension(projectFile);
            return LooksLikeTestProjectName(fileName) ||
                   text.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("MSTest.Sdk", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("MSTest.TestFramework", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("NUnit", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("coverlet.collector", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTestSource(string filePath, string text)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            return LooksLikeTestProjectName(fileName) ||
                   HasTestPathSegment(filePath) ||
                   LooksLikeTestSource(text);
        }

        private static bool LooksLikeTestProjectName(string value)
        {
            return Regex.IsMatch(value, @"(^|[._-])(Test|Tests|UnitTests|IntegrationTests|FunctionalTests|AcceptanceTests|Spec|Specs)([._-]|$)", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(value, @"(Tests|Specs)$", RegexOptions.IgnoreCase);
        }

        private static bool HasTestPathSegment(string filePath)
        {
            return filePath
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => Regex.IsMatch(segment, @"^(Tests?|UnitTests|IntegrationTests|FunctionalTests|AcceptanceTests|Specs?)$", RegexOptions.IgnoreCase));
        }

        private static bool LooksLikeTestSource(string text)
        {
            if (text.Contains("Microsoft.VisualStudio.TestTools.UnitTesting", StringComparison.Ordinal) ||
                text.Contains("using Xunit", StringComparison.Ordinal) ||
                text.Contains("using NUnit.Framework", StringComparison.Ordinal) ||
                text.Contains("namespace Xunit", StringComparison.Ordinal) ||
                text.Contains("namespace NUnit", StringComparison.Ordinal))
            {
                return true;
            }

            return Regex.IsMatch(
                text,
                @"\[(?:Fact|Theory|Test|TestCase|TestMethod|TestClass|TestFixture|SetUp|OneTimeSetUp|TearDown|OneTimeTearDown)\b",
                RegexOptions.IgnoreCase);
        }

        private static IEnumerable<string>? EnumerateGitVisibleFiles(string root)
        {
            try
            {
                var repositoryRoot = RunGit(root, "rev-parse", "--show-toplevel").Trim();
                if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
                {
                    return null;
                }

                var output = RunGit(root, "ls-files", "--cached", "--others", "--exclude-standard", "--full-name", "--", ".");
                var files = output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(path => Path.GetFullPath(Path.Combine(repositoryRoot, path)))
                    .Where(File.Exists)
                    .ToArray();

                return files.Length == 0 ? null : files;
            }
            catch
            {
                return null;
            }
        }

        private static string RunGit(string workingDirectory, params string[] arguments)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(error);
            }

            return output;
        }

        private static bool IsEntityFrameworkMigrationSource(string text)
        {
            if (!LooksLikeEntityFrameworkMigrationSource(text))
            {
                return false;
            }

            try
            {
                var root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
                var types = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();
                foreach (var type in types)
                {
                    if (HasBaseType(type, "Migration") || HasBaseType(type, "ModelSnapshot"))
                    {
                        return true;
                    }
                }

                var attributes = root.DescendantNodes().OfType<AttributeSyntax>().Select(a => a.Name.ToString()).ToArray();
                return attributes.Any(IsMigrationAttribute) && attributes.Any(IsDbContextAttribute);
            }
            catch
            {
                return Regex.IsMatch(text, @"class\s+[A-Za-z0-9_]+\s*:\s*(?:[A-Za-z0-9_.]+\.)?(?:Migration|ModelSnapshot)\b") ||
                       (text.Contains("[Migration(", StringComparison.Ordinal) &&
                        text.Contains("[DbContext(", StringComparison.Ordinal));
            }
        }

        private static bool LooksLikeEntityFrameworkMigrationSource(string text)
        {
            return text.Contains("MigrationBuilder", StringComparison.Ordinal) ||
                   text.Contains("ModelSnapshot", StringComparison.Ordinal) ||
                   text.Contains("BuildTargetModel", StringComparison.Ordinal) ||
                   text.Contains("[Migration(", StringComparison.Ordinal) ||
                   text.Contains("Microsoft.EntityFrameworkCore.Migrations", StringComparison.Ordinal) ||
                   text.Contains(": Migration", StringComparison.Ordinal) ||
                   text.Contains(":Migration", StringComparison.Ordinal);
        }

        private static bool HasBaseType(BaseTypeDeclarationSyntax type, string name)
        {
            return type.BaseList?.Types.Any(baseType =>
            {
                var baseName = baseType.Type.ToString();
                return baseName.Equals(name, StringComparison.Ordinal) ||
                       baseName.EndsWith("." + name, StringComparison.Ordinal);
            }) == true;
        }

        private static bool IsMigrationAttribute(string name)
        {
            return name.Equals("Migration", StringComparison.Ordinal) ||
                   name.Equals("MigrationAttribute", StringComparison.Ordinal) ||
                   name.EndsWith(".Migration", StringComparison.Ordinal) ||
                   name.EndsWith(".MigrationAttribute", StringComparison.Ordinal);
        }

        private static bool IsDbContextAttribute(string name)
        {
            return name.Equals("DbContext", StringComparison.Ordinal) ||
                   name.Equals("DbContextAttribute", StringComparison.Ordinal) ||
                   name.EndsWith(".DbContext", StringComparison.Ordinal) ||
                   name.EndsWith(".DbContextAttribute", StringComparison.Ordinal);
        }
    }
}
