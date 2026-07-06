# Changelog

Formato: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versionamento [SemVer](https://semver.org/spec/v2.0.0.html).

## [0.2.4] - 2026-07-06

Hotfix patch aligning `AiMemory.Tool` and `AiMemory.Tray` versions. Pending changes had been committed on the `release/v0.2.0` branch after the v0.2.0 release without a CHANGELOG entry, version bump, or git tag. This release narrates and ships them.

### Fixed
- MCP server not responding to `initialize`/`tools/list`, causing every MCP client (opencode/Rider/VS Code/Cursor/Claude Desktop/Antigravity/Codex) to time out after 30s. The v0.2.0 change that redirected `Console.Out` to `TextWriter.Null` to protect the JSON-RPC stream also swallowed the JSON-RPC responses themselves, which were written to `Console.Out`. Responses are now written to a captured `_stdout` reference taken before redirection; writing helpers became instance-bound to reach it.
- Tray asset URI paths for the `active`/`idle` icons (so the tray icon shows correctly across all platforms).

### Added
- `setup-mcp.sh` at repo root: idempotent registration of the `ai-memory` MCP server in Claude Desktop/Claude (`~/.config/claude/mcp.json`), Antigravity/VS Code (`~/.config/Antigravity/User/settings.json`), Cursor (`~/.cursor/mcp.json`) and a notice for opencode (`~/.config/opencode/opencode.jsonc`).
- Project-level opencode config (`.opencode/opencode.jsonc`) and IDE-agnostic MCP registration (`.ai/mcp/mcp.json`) registering `ai-memory mcp` as a local stdio server.
- MCP configuration updated to use the local "type" and enable command execution.

### Changed
- `AiMemory.Tray` bumped to 0.2.4 (was 0.2.3, untagged) to keep both packages version-aligned.
- README rewritten from a deep code audit: commands, MCP tools, schema, chunking, configuration, tray, tests, CI/CD, roadmap and limitations now reflect the actual implementation.

## [0.2.0] - 2026-07-06

### Added
- MCP protocol upgraded to 2025-11-25 with Tool Annotations (`readOnlyHint: true`), Structured Output, icons, `title` field, and Tool Execution Errors
- Batch embedding via Ollama `/api/embed` endpoint
- Parallel extraction for `index rules` and `index knowledge` with `--parallelism` flag
- Polly retry with exponential backoff + jitter on all Ollama calls
- Real `FileSystemWatcher` in `ai-memory watch` with 500ms debounce
- LRU embedding cache (MemoryCache, 1000 entries, 1h TTL) replacing FIFO Dictionary
- Configurable business rule/knowledge patterns via `~/.aimemory/patterns.json`
- CI/CD pipelines (GitHub Actions): build/test on PR, NuGet publish on tag
- Orphan chunk cleanup after indexing
- 35 unit tests covering core services
- Tray split into separate `AiMemory.Tray` NuGet package

### Changed
- SQL migrations renamed for deterministic ordering: `000_schema.sql`, `010_compat.sql`, `020_hybrid_search.sql`
- `MaxChunkLength` unified at 6000 across code and documentation
- MCP stdout redirected to `TextWriter.Null` to prevent JSON-RPC stream corruption
- `PgVectorService` split into 8 focused repository classes

### Fixed
- Build failure on Linux/macOS due to `Directory.SetUnixFileMode` targeting pack mismatch (now uses reflection + `chmod` fallback)

### Removed
- Avalonia UI dependencies from main `AiMemory.Tool` package (now in separate `AiMemory.Tray`)

## [0.1.5] - 2026-??

### Added
- Setup interativo cross-platform (Linux, macOS, Windows)
- Indexação por fases: chunks, rules, knowledge
- Extração heurística e semântica de regras de negócio e conhecimento
- Busca híbrida (RRF) com reranker heurístico
- Grafo de símbolos (Roslyn) com MCP tools `get_symbol_callers`, `get_symbol_callees`, `get_class_hierarchy`
- Compressão de contexto (estilo Netflix Headroom)
- Contextual chunking com âncora semântica
- Servidor MCP STDIO com `search_code`, `search_business_rules`, `search_knowledge`, `find_related_files`
- Dashboard terminal e web (`dashboard serve`)
- Tray application com Avalonia (ícone dinâmico, autostart cross-platform)
- Pacote NuGet como .NET global tool

### Known Issues
- Build quebra em alguns ambientes (`Directory.SetUnixFileMode`)
- Chunks órfãos não são limpos após deleção de arquivos
- Migrations SQL com prefixos duplicados
- Sem testes automatizados
- MCP em versão `2024-11-05` (desatualizada)

[0.2.0]: https://github.com/ernanesa/ai-memory/compare/v0.1.5...v0.2.0
[0.1.5]: https://github.com/ernanesa/ai-memory/releases/tag/v0.1.5
