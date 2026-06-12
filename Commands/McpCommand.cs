using System.Text.Json;
using AiMemory.Configuration;
using AiMemory.Services;

namespace AiMemory.Commands;

public static class McpCommand
{
    private const string ProtocolVersion = "2024-11-05";

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

        var server = new McpServer(connectionString, ollamaBaseUrl, embeddingModel);
        await server.RunAsync();
    }

    private sealed class McpServer(string connectionString, string ollamaBaseUrl, string embeddingModel)
    {
        private readonly OllamaService _ollama = new(ollamaBaseUrl, embeddingModel);

        public async Task RunAsync(CancellationToken ct = default)
        {
            Console.Error.WriteLine("AI Memory MCP server started over stdio.");

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
                    await WriteToolErrorAsync(id, ex.Message, ct);
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
                    version = "0.1.2"
                },
                instructions = "Use ai-memory tools to retrieve indexed local engineering context before answering questions about configured projects."
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
                "find_related_files" => await FindRelatedFilesAsync(arguments, ct),
                _ => throw new ArgumentException($"Unknown tool: {toolName}")
            };
        }

        private async Task<object> SearchCodeAsync(JsonElement arguments, CancellationToken ct)
        {
            var query = GetRequiredString(arguments, "query");
            var limit = GetOptionalInt(arguments, "limit", 10, 1, 50);
            var project = GetOptionalString(arguments, "project");
            var maxContentChars = GetOptionalInt(arguments, "max_content_chars", 1200, 200, 10000);

            var embedding = await _ollama.EmbedAsync(query, ct);
            await using var pg = new PgVectorService(connectionString);
            var results = await pg.SearchCodeAsync(embedding, limit, project, ct);

            var payload = results.Select(r => new
            {
                r.Project,
                r.File,
                r.Language,
                r.ChunkType,
                r.Symbol,
                r.Distance,
                Content = Truncate(r.Content, maxContentChars)
            }).ToList();

            return ToolJsonResult(payload);
        }

        private async Task<object> SearchBusinessRulesAsync(JsonElement arguments, CancellationToken ct)
        {
            var query = GetRequiredString(arguments, "query");
            var limit = GetOptionalInt(arguments, "limit", 10, 1, 50);
            var project = GetOptionalString(arguments, "project");

            var embedding = await _ollama.EmbedAsync(query, ct);
            await using var pg = new PgVectorService(connectionString);
            var results = await pg.SearchBusinessRulesAsync(embedding, limit, project, ct);
            return ToolJsonResult(results);
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

            await using var pg = new PgVectorService(connectionString);
            string embeddingText;
            if (!string.IsNullOrWhiteSpace(query))
            {
                embeddingText = query;
            }
            else
            {
                var chunks = await pg.GetFileChunksAsync(file!, project, 12000, ct);
                if (chunks.Count == 0)
                {
                    throw new ArgumentException($"No indexed chunks found for file: {file}");
                }

                embeddingText = string.Join("\n\n", chunks.Select(c => c.Content));
            }

            var embedding = await _ollama.EmbedAsync(embeddingText, ct);
            var results = await pg.FindRelatedFilesAsync(embedding, limit, project, file, ct);
            return ToolJsonResult(results);
        }

        private static object[] CreateTools()
        {
            return
            [
                new
                {
                    name = "search_code",
                    description = "Search indexed code, documentation and configuration chunks using semantic similarity.",
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
                    }
                },
                new
                {
                    name = "search_business_rules",
                    description = "Search indexed business rules using semantic similarity.",
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
                    }
                },
                new
                {
                    name = "find_related_files",
                    description = "Find files related to a query or to an already indexed file.",
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
                    }
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
                }
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
