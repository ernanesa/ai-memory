using System.Text.RegularExpressions;

namespace AiMemory.Services
{
    public static class ContextCompressionService
    {
        private static readonly Regex UsingsRegex = new(
            @"^using\s+[A-Za-z0-9_.]+;\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex LicenseHeaderRegex = new(
            @"(?i)(?:copyright|license|all\s+rights\s+reserved|auto-generated|created\s+by\s+code\s+generator)",
            RegexOptions.Compiled);

        public static string CompressCode(string content, string? language, string? symbolName)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            var lines = content.Split('\n');
            var cleanedLines = new List<string>();
            var inUsingsBlock = false;
            var usingsCount = 0;
            var inLicenseHeader = false;
            var licenseHeaderLines = new List<int>();

            // Step 1: Detect and mark comment headers (like copyrights/licenses) at the beginning of the file
            for (var i = 0; i < Math.Min(lines.Length, 35); i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*"))
                {
                    if (LicenseHeaderRegex.IsMatch(line))
                    {
                        inLicenseHeader = true;
                    }

                    if (inLicenseHeader)
                    {
                        licenseHeaderLines.Add(i);
                    }
                }
                else if (line.Length > 0 && !line.StartsWith("*/"))
                {
                    break;
                }
            }

            // Step 2: Process line-by-line
            for (var i = 0; i < lines.Length; i++)
            {
                if (licenseHeaderLines.Contains(i))
                {
                    if (i == licenseHeaderLines[0])
                    {
                        cleanedLines.Add("// [license header omitted]");
                    }
                    continue;
                }

                var line = lines[i];
                var trimmedLine = line.Trim();

                // Handle C# usings block collapsing
                var isUsing = UsingsRegex.IsMatch(trimmedLine);
                if (isUsing)
                {
                    if (!inUsingsBlock)
                    {
                        inUsingsBlock = true;
                        usingsCount = 1;
                    }
                    else
                    {
                        usingsCount++;
                    }
                    continue;
                }
                else
                {
                    if (inUsingsBlock)
                    {
                        if (usingsCount > 3)
                        {
                            cleanedLines.Add("// [usings omitted]");
                        }
                        else
                        {
                            // Add back the small number of usings
                            for (var j = i - usingsCount; j < i; j++)
                            {
                                cleanedLines.Add(lines[j].TrimEnd());
                            }
                        }
                        inUsingsBlock = false;
                    }
                }

                // Omit XML comments that are excessively verbose
                if (trimmedLine.StartsWith("/// <summary>") || trimmedLine.StartsWith("/// <param") || trimmedLine.StartsWith("/// <returns>"))
                {
                    continue;
                }
                if (trimmedLine.StartsWith("/// </summary>"))
                {
                    continue;
                }
                if (trimmedLine.StartsWith("/// "))
                {
                    var commentText = trimmedLine[4..].Trim();
                    if (commentText.Length > 120)
                    {
                        cleanedLines.Add("/// " + commentText[..117] + "...");
                        continue;
                    }
                }

                // Omit method bodies for non-matching symbols if symbolName is provided
                if (!string.IsNullOrWhiteSpace(symbolName))
                {
                    var simpleSymbolName = GetSimpleSymbolName(symbolName);
                    var isOtherMethodHeader = IsMethodHeader(trimmedLine) && !trimmedLine.Contains(simpleSymbolName, StringComparison.OrdinalIgnoreCase);
                    if (isOtherMethodHeader)
                    {
                        if (trimmedLine.EndsWith('{'))
                        {
                            cleanedLines.Add(line.TrimEnd() + " /* body omitted */ }");
                            i = SkipMethodBody(lines, i);
                            continue;
                        }
                        else if (i + 1 < lines.Length && lines[i + 1].Trim() == "{")
                        {
                            cleanedLines.Add(line.TrimEnd() + " { /* body omitted */ }");
                            i = SkipMethodBody(lines, i + 1);
                            continue;
                        }
                    }
                }

                cleanedLines.Add(line.TrimEnd());
            }

            if (inUsingsBlock && usingsCount > 3)
            {
                cleanedLines.Add("// [usings omitted]");
            }

            // Step 3: Remove duplicate consecutive empty lines
            var resultLines = new List<string>();
            var consecutiveEmptyLines = 0;
            foreach (var line in cleanedLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    consecutiveEmptyLines++;
                    if (consecutiveEmptyLines == 1)
                    {
                        resultLines.Add("");
                    }
                }
                else
                {
                    consecutiveEmptyLines = 0;
                    resultLines.Add(line);
                }
            }

            return string.Join("\n", resultLines).Trim();
        }

        public static string CompressEvidence(string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence))
            {
                return evidence;
            }

            var lines = evidence.Split('\n')
                .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
                .Where(line => line.Length > 0);

            return string.Join("\n", lines);
        }

        private static bool IsMethodHeader(string trimmedLine)
        {
            return (trimmedLine.StartsWith("public ") || trimmedLine.StartsWith("private ") || trimmedLine.StartsWith("internal ") || trimmedLine.StartsWith("protected ") || trimmedLine.StartsWith("async ") || trimmedLine.StartsWith("static ")) &&
                   (trimmedLine.Contains('(') && trimmedLine.Contains(')') && !trimmedLine.Contains("class ") && !trimmedLine.Contains("interface ") && !trimmedLine.Contains("record ") && !trimmedLine.Contains("namespace "));
        }

        private static int SkipMethodBody(string[] lines, int startIndex)
        {
            var braceCount = 1;
            var i = startIndex + 1;
            for (; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var ch in line)
                {
                    if (ch == '{') braceCount++;
                    else if (ch == '}') braceCount--;
                }

                if (braceCount <= 0)
                {
                    break;
                }
            }
            return i;
        }

        private static string GetSimpleSymbolName(string symbolName)
        {
            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return symbolName;
            }

            var parenIndex = symbolName.IndexOf('(');
            var baseName = parenIndex >= 0 ? symbolName[..parenIndex] : symbolName;

            var genericIndex = baseName.IndexOf('<');
            baseName = genericIndex >= 0 ? baseName[..genericIndex] : baseName;

            var lastDot = baseName.LastIndexOf('.');
            var simpleName = lastDot >= 0 ? baseName[(lastDot + 1)..] : baseName;

            return simpleName.Trim();
        }
    }
}
