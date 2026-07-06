using System.Text.Json;
using System.Reflection;
using AiMemory.Configuration;
using AiMemory.Services;
using Microsoft.Extensions.Caching.Memory;

namespace AiMemory.Commands
{
    public static class McpCommand
    {
        private const string ProtocolVersion = "2025-11-25";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static async Task RunAsync(string? db, string? ollama, string? model)
        {
            var config = await ConfigService.LoadAsync();
            var connectionString = ConfigService.ResolveConnectionString(config, db);
            var ollamaBaseUrl = ConfigService.ResolveOllamaBaseUrl(config, ollama);
            var embeddingModel = ConfigService.ResolveEmbeddingModel(config, model);

            await using var server = new McpServer(connectionString, ollamaBaseUrl, embeddingModel);
            await server.RunAsync();
        }

        private sealed class McpServer(string connectionString, string ollamaBaseUrl, string embeddingModel) : IAsyncDisposable
        {
            private readonly OllamaService _ollama = new(ollamaBaseUrl, embeddingModel);
            private readonly PgVectorService _pg = new(connectionString);
            private readonly MemoryCache _embeddingCache = new(new MemoryCacheOptions
            {
                SizeLimit = 1000,
                ExpirationScanFrequency = TimeSpan.FromMinutes(5)
            });

            public async ValueTask DisposeAsync()
            {
                await _pg.DisposeAsync();
            }

            public async Task RunAsync(CancellationToken ct = default)
            {
                Console.Error.WriteLine("AI Memory MCP server started over stdio.");

                var originalOut = Console.Out;
                Console.SetOut(TextWriter.Null);
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var line = await Console.In.ReadLineAsync(ct);
                        if (line is null)
                        {
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        await HandleMessageAsync(line, ct);
                    }
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }

            private async Task HandleMessageAsync(string line, CancellationToken ct)
            {
                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException ex)
                {
                    await WriteErrorAsync(null, -32700, $"Parse error: {ex.Message}", ct);
                    return;
                }

                using (document)
                {
                    var root = document.RootElement;
                    var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
                    var method = root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String
                        ? methodElement.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(method))
                    {
                        if (id is not null)
                        {
                            await WriteErrorAsync(id, -32600, "Invalid request: missing method.", ct);
                        }

                        return;
                    }

                    if (method.StartsWith("notifications/", StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (id is null)
                    {
                        return;
                    }

                    try
                    {
                        var result = method switch
                        {
                            "initialize" => CreateInitializeResult(root),
                            "ping" => new { },
                            "tools/list" => new { tools = CreateTools() },
                            "tools/call" => await CallToolAsync(root, ct),
                            "resources/list" => new { resources = Array.Empty<object>() },
                            "prompts/list" => new { prompts = Array.Empty<object>() },
                            _ => null
                        };

                        if (result is null)
                        {
                            await WriteErrorAsync(id, -32601, $"Method not found: {method}", ct);
                            return;
                        }

                        await WriteResultAsync(id, result, ct);
                    }
                    catch (ArgumentException ex)
                    {
                        await WriteResultAsync(id, new { content = new[] { new { type = "text", text = ex.Message } }, isError = true }, ct);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                        await WriteToolErrorAsync(id, ex.Message, ct);
                    }
                }
            }

            private static object CreateInitializeResult(JsonElement request)
            {
                var clientProtocolVersion = TryGetProperty(request, "params", out var parameters) &&
                                            TryGetString(parameters, "protocolVersion", out var requestedVersion)
                    ? requestedVersion
                    : ProtocolVersion;

                return new
                {
                    protocolVersion = string.IsNullOrWhiteSpace(clientProtocolVersion) ? ProtocolVersion : clientProtocolVersion,
                    capabilities = new
                    {
                        tools = new
                        {
                            listChanged = false
                        },
                        resources = new { },
                        prompts = new { }
                    },
                    serverInfo = new
                    {
                        name = "ai-memory",
                        version = typeof(McpCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                  ?? typeof(McpCommand).Assembly.GetName().Version?.ToString()
                                  ?? "0.0.0"
                    },
                    instructions = "Use ai-memory tools in this order: 1) Call search_code with specific symbols, error messages or domain terms BEFORE reading files from disk. 2) Call search_business_rules when the topic involves domain behavior, validations, permissions or workflows. 3) Call search_knowledge when the topic involves architecture, integrations, patterns or technical decisions. 4) Call find_related_files to discover connected files before refactoring. 5) Only read files directly when ai-memory results are insufficient. Results include distance scores where lower is more relevant (0=exact match, >0.5=weak match). Prefer results with distance < 0.4."
                };
            }

            private async Task<object> CallToolAsync(JsonElement request, CancellationToken ct)
            {
                if (!TryGetProperty(request, "params", out var parameters))
                {
                    throw new ArgumentException("Missing params.");
                }

                if (!TryGetString(parameters, "name", out var toolName) || string.IsNullOrWhiteSpace(toolName))
                {
                    throw new ArgumentException("Missing tool name.");
                }

                var arguments = TryGetProperty(parameters, "arguments", out var args) && args.ValueKind == JsonValueKind.Object
                    ? args
                    : default;

                return toolName switch
                {
                    "search_code" => await SearchCodeAsync(arguments, ct),
                    "search_business_rules" => await SearchBusinessRulesAsync(arguments, ct),
                    "search_knowledge" => await SearchKnowledgeAsync(arguments, ct),
                    "find_related_files" => await FindRelatedFilesAsync(arguments, ct),
                    "get_symbol_callers" => await GetSymbolCallersAsync(arguments, ct),
                    "get_symbol_callees" => await GetSymbolCalleesAsync(arguments, ct),
                    "get_class_hierarchy" => await GetClassHierarchyAsync(arguments, ct),
                    _ => throw new ArgumentException($"Unknown tool: {toolName}")
                };
            }

            private async Task<object> SearchCodeAsync(JsonElement arguments, CancellationToken ct)
            {
                var query = GetRequiredString(arguments, "query");
                var limit = GetOptionalInt(arguments, "limit", 10, 1, 50);
                var project = GetOptionalString(arguments, "project");
                var maxContentChars = GetOptionalInt(arguments, "max_content_chars", 1200, 200, 10000);

                var embedding = await EmbedCachedAsync(query, ct);
                var results = await _pg.SearchCodeAsync(embedding, query, limit, project, ct);
                var reranked = RerankerService.RerankCode(results, query);

                var payload = reranked.Select(r => new
                {
                    r.Project,
                    r.File,
                    r.Language,
                    r.ChunkType,
                    r.Symbol,
                    r.Distance,
                    Content = Truncate(ContextCompressionService.CompressCode(r.Content, r.Language, r.Symbol), maxContentChars)
                }).ToList();

                return ToolJsonResult(payload);
            }

            private async Task<object> SearchBusinessRulesAsync(JsonElement arguments, CancellationToken ct)
            {
                var query = GetRequiredString(arguments, "query");
                var limit = GetOptionalInt(arguments, "limit", 10, 1, 50);
                var project = GetOptionalString(arguments, "project");

                var embedding = await EmbedCachedAsync(query, ct);
                var results = await _pg.SearchBusinessRulesAsync(embedding, query, limit, project, ct);
                var payload = results.Select(r => new
                {
                    r.Project,
                    r.Title,
                    r.Description,
                    r.SourceFile,
                    r.Symbol,
                    r.Status,
                    Evidence = ContextCompressionService.CompressEvidence(r.Evidence ?? ""),
                    r.Confidence,
                    r.Distance
                }).ToList();
                return ToolJsonResult(payload);
            }

            private async Task<object> SearchKnowledgeAsync(JsonElement arguments, CancellationToken ct)
            {
                var query = GetRequiredString(arguments, "query");
                var limit = GetOptionalInt(arguments, "limit", 10, 1, 50);
                var project = GetOptionalString(arguments, "project");

                var embedding = await EmbedCachedAsync(query, ct);
                var results = await _pg.SearchKnowledgeAsync(embedding, query, limit, project, ct);
                var payload = results.Select(r => new
                {
                    r.Project,
                    r.Kind,
                    r.Title,
                    r.Content,
                    r.Source,
                    r.Symbol,
                    r.Status,
                    Evidence = ContextCompressionService.CompressEvidence(r.Evidence ?? ""),
                    r.Confidence,
                    r.Distance
                }).ToList();
                return ToolJsonResult(payload);
            }

            private async Task<object> GetSymbolCallersAsync(JsonElement arguments, CancellationToken ct)
            {
                var symbol = GetRequiredString(arguments, "symbol");
                var project = GetOptionalString(arguments, "project");

                var results = await _pg.GetSymbolCallersAsync(symbol, project, ct);
                return ToolJsonResult(results.Select(r => new { Project = r.Project, Symbol = r.Symbol, File = r.File, Relation = r.Relation }));
            }

            private async Task<object> GetSymbolCalleesAsync(JsonElement arguments, CancellationToken ct)
            {
                var symbol = GetRequiredString(arguments, "symbol");
                var project = GetOptionalString(arguments, "project");

                var results = await _pg.GetSymbolCalleesAsync(symbol, project, ct);
                return ToolJsonResult(results.Select(r => new { Project = r.Project, Symbol = r.Symbol, File = r.File, Relation = r.Relation }));
            }

            private async Task<object> GetClassHierarchyAsync(JsonElement arguments, CancellationToken ct)
            {
                var className = GetRequiredString(arguments, "className");
                var project = GetOptionalString(arguments, "project");

                var results = await _pg.GetClassHierarchyAsync(className, project, ct);
                return ToolJsonResult(results.Select(r => new { Project = r.Project, ParentName = r.ParentName, Relation = r.Relation }));
            }

            private async Task<object> FindRelatedFilesAsync(JsonElement arguments, CancellationToken ct)
            {
                var file = GetOptionalString(arguments, "file");
                var query = GetOptionalString(arguments, "query");
                var project = GetOptionalString(arguments, "project");
                var limit = GetOptionalInt(arguments, "limit", 10, 1, 50);

                if (string.IsNullOrWhiteSpace(file) && string.IsNullOrWhiteSpace(query))
                {
                    throw new ArgumentException("Provide either 'file' or 'query'.");
                }

                string embeddingText;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    embeddingText = query;
                }
                else
                {
                    var chunks = await _pg.GetFileChunksAsync(file!, project, 12000, ct);
                    if (chunks.Count == 0)
                    {
                        throw new ArgumentException($"No indexed chunks found for file: {file}");
                    }

                    embeddingText = string.Join("\n\n", chunks.Select(c => c.Content));
                }

                var embedding = await EmbedCachedAsync(embeddingText, ct);
                var results = await _pg.FindRelatedFilesAsync(embedding, limit, project, file, ct);
                return ToolJsonResult(results);
            }

            private async Task<float[]> EmbedCachedAsync(string text, CancellationToken ct)
            {
                var key = $"{embeddingModel}\n{text}";
                if (_embeddingCache.TryGetValue(key, out float[]? cached) && cached is not null)
                {
                    return cached;
                }

                var embedding = await _ollama.EmbedAsync(text, ct);
                _embeddingCache.Set(key, embedding, new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                });
                return embedding;
            }

            private static object[] CreateTools()
            {
                const string iconSrc = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgc3Ryb2tlPSJjdXJyZW50Q29sb3IiIHN0cm9rZS13aWR0aD0iMiI+PGNpcmNsZSBjeD0iMTIiIGN5PSIxMiIgcj0iMTAiLz48L3N2Zz4=";

                object outputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["results"] = new { type = "array" }
                    }
                };

                object[] icons = [new { src = iconSrc }];

                return
                [
                    new
                    {
                        name = "search_code",
                        title = "Search Code",
                        description = "Search indexed code, docs and config. Call this BEFORE reading repository files. Returns ranked chunks with distance scores. Use specific symbols, error messages, or domain terms for best results.",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["query"] = new { type = "string", description = "Natural language or code-oriented search query." },
                                ["limit"] = new { type = "integer", description = "Maximum number of chunks to return.", minimum = 1, maximum = 50, @default = 10 },
                                ["project"] = new { type = "string", description = "Optional project filter. Accepts exact project name or workspace/project." },
                                ["max_content_chars"] = new { type = "integer", description = "Maximum characters returned per chunk.", minimum = 200, maximum = 10000, @default = 1200 }
                            },
                            required = new[] { "query" }
                        },
                        annotations = new { readOnlyHint = true },
                        outputSchema,
                        icons
                    },
                    new
                    {
                        name = "search_business_rules",
                        title = "Search Business Rules",
                        description = "Search domain rules, validations and constraints extracted from code. Call when the question involves business logic, permissions, statuses, or workflows. Returns rules with evidence and confidence scores.",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["query"] = new { type = "string", description = "Business question or rule description to search for." },
                                ["limit"] = new { type = "integer", description = "Maximum number of rules to return.", minimum = 1, maximum = 50, @default = 10 },
                                ["project"] = new { type = "string", description = "Optional project filter. Accepts exact project name or workspace/project." }
                            },
                            required = new[] { "query" }
                        },
                        annotations = new { readOnlyHint = true },
                        outputSchema,
                        icons
                    },
                    new
                    {
                        name = "find_related_files",
                        title = "Find Related Files",
                        description = "Discover files semantically connected to a concept or file. Call to understand impact radius before refactoring. Accepts a file path or semantic query as input.",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["file"] = new { type = "string", description = "Indexed source file path. Used as the semantic seed when query is omitted." },
                                ["query"] = new { type = "string", description = "Optional semantic query. When provided, it is used instead of file content." },
                                ["limit"] = new { type = "integer", description = "Maximum number of files to return.", minimum = 1, maximum = 50, @default = 10 },
                                ["project"] = new { type = "string", description = "Optional project filter. Accepts exact project name or workspace/project." }
                            }
                        },
                        annotations = new { readOnlyHint = true },
                        outputSchema,
                        icons
                    },
                    new
                    {
                        name = "search_knowledge",
                        title = "Search Knowledge",
                        description = "Search engineering knowledge: architectural decisions, integration patterns, technical risks, configurations and conventions extracted from code. Call when the question involves architecture, infrastructure, dependencies, messaging, authentication or technical patterns.",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["query"] = new { type = "string", description = "Technical topic, pattern, integration or architectural question to search for." },
                                ["limit"] = new { type = "integer", description = "Maximum number of knowledge entries to return.", minimum = 1, maximum = 50, @default = 10 },
                                ["project"] = new { type = "string", description = "Optional project filter. Accepts exact project name or workspace/project." }
                            },
                            required = new[] { "query" }
                        },
                        annotations = new { readOnlyHint = true },
                        outputSchema,
                        icons
                    },
                    new
                    {
                        name = "get_symbol_callers",
                        title = "Get Symbol Callers",
                        description = "Find C# callers of a specific symbol in the code graph (e.g. what methods call MyMethod).",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["symbol"] = new { type = "string", description = "Name of the method or symbol to find callers for." },
                                ["project"] = new { type = "string", description = "Optional project filter." }
                            },
                            required = new[] { "symbol" }
                        },
                        annotations = new { readOnlyHint = true },
                        outputSchema,
                        icons
                    },
                    new
                    {
                        name = "get_symbol_callees",
                        title = "Get Symbol Callees",
                        description = "Find C# callees of a specific symbol in the code graph (e.g. what methods MyMethod calls).",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["symbol"] = new { type = "string", description = "Name of the method or symbol to find callees for." },
                                ["project"] = new { type = "string", description = "Optional project filter." }
                            },
                            required = new[] { "symbol" }
                        },
                        annotations = new { readOnlyHint = true },
                        outputSchema,
                        icons
                    },
                    new
                    {
                        name = "get_class_hierarchy",
                        title = "Get Class Hierarchy",
                        description = "Get C# class inheritance or interface implementations (e.g. parent classes or interfaces of MyClass).",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["className"] = new { type = "string", description = "Name of the class or interface." },
                                ["project"] = new { type = "string", description = "Optional project filter." }
                            },
                            required = new[] { "className" }
                        },
                        annotations = new { readOnlyHint = true },
                        outputSchema,
                        icons
                    }
                ];
            }

            private static object ToolJsonResult<T>(T payload)
            {
                return new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonOptions) { WriteIndented = true })
                        }
                    },
                    structuredContent = payload
                };
            }

            private static async Task WriteResultAsync(JsonElement? id, object result, CancellationToken ct)
            {
                await WriteMessageAsync(new { jsonrpc = "2.0", id, result }, ct);
            }

            private static async Task WriteToolErrorAsync(JsonElement? id, string message, CancellationToken ct)
            {
                await WriteResultAsync(id, new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = message
                        }
                    },
                    isError = true
                }, ct);
            }

            private static async Task WriteErrorAsync(JsonElement? id, int code, string message, CancellationToken ct)
            {
                await WriteMessageAsync(new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new
                    {
                        code,
                        message
                    }
                }, ct);
            }

            private static async Task WriteMessageAsync(object message, CancellationToken ct)
            {
                var json = JsonSerializer.Serialize(message, JsonOptions);
                await Console.Out.WriteLineAsync(json.AsMemory(), ct);
                await Console.Out.FlushAsync(ct);
            }

            private static string GetRequiredString(JsonElement arguments, string name)
            {
                var value = GetOptionalString(arguments, name);
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException($"Missing required argument: {name}");
                }

                return value;
            }

            private static string? GetOptionalString(JsonElement arguments, string name)
            {
                return arguments.ValueKind == JsonValueKind.Object &&
                       arguments.TryGetProperty(name, out var value) &&
                       value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            }

            private static int GetOptionalInt(JsonElement arguments, string name, int defaultValue, int minimum, int maximum)
            {
                if (arguments.ValueKind != JsonValueKind.Object ||
                    !arguments.TryGetProperty(name, out var value) ||
                    value.ValueKind != JsonValueKind.Number ||
                    !value.TryGetInt32(out var parsed))
                {
                    return defaultValue;
                }

                return Math.Clamp(parsed, minimum, maximum);
            }

            private static bool TryGetString(JsonElement element, string name, out string? value)
            {
                if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    value = property.GetString();
                    return true;
                }

                value = null;
                return false;
            }

            private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out property))
                {
                    return true;
                }

                property = default;
                return false;
            }

            private static string Truncate(string value, int maxLength)
            {
                return value.Length <= maxLength ? value : value[..maxLength] + "...";
            }
        }
    }
}
