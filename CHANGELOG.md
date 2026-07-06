# Changelog

Formato: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versionamento [SemVer](https://semver.org/spec/v2.0.0.html).

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

### Fixed
- Build failure on Linux/macOS due to `Directory.SetUnixFileMode` targeting pack mismatch (now uses reflection + `chmod` fallback)

### Removed
- Avalonia UI dependencies from main `AiMemory.Tool` package (now in separate `AiMemory.Tray`)

### Changed
- `PgVectorService` split into 8 focused repository classes

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
