# User Stories

Formato: "As a [persona], I want [feature], so that [benefit]." Critérios de aceitação em Given/When/Then quando aplicável.

## US-001 — Corrigir build quebrado (PB-001)

**As** Ernane (P1),
**I want** `dotnet build` verde em Linux/macOS/Windows,
**so that** qualquer máquina consegue compilar o projeto do zero.

### Critérios de aceitação
- **Given** ambiente Linux com .NET 10 SDK
- **When** executo `dotnet build -c Debug`
- **Then** build verde com 0 erros e 0 warnings
- **And** `dotnet build -c Release` também verde
- **And** `OperatingSystem.IsWindows()` continua protegendo chamadas Windows-only em runtime

### Notas técnicas
- `ConfigService.cs:300` — envolver `Directory.SetUnixFileMode` com `#if` + `[SupportedOSPlatform("linux")]` ou reflexão
- Verificar outras chamadas Unix-only no mesmo arquivo

---

## US-002 — Limpar chunks órfãos (PB-002)

**As** Ernane (P1),
**I want** chunks de arquivos deletados sejam removidos do banco após indexação,
**so that** busca não retorna código que não existe mais.

### Critérios de aceitação
- **Given** projeto indexado com arquivo `Foo.cs`
- **When** deleto `Foo.cs` e rodo `ai-memory index chunks`
- **Then** chunks de `Foo.cs` são removidos do banco
- **And** `search_code` não retorna mais `Foo.cs`
- **And** log mostra "removidos N chunks órfãos"
- **And** regras/knowledge que referenciavam esses chunks continuam válidas (não cascade delete)

### Notas técnicas
- Após upsert de cada projeto, rodar `DELETE FROM ai_chunks WHERE project_id = $1 AND file_path NOT IN (arquivos atuais)`
- Considerar `ai_business_rules` e `ai_knowledge` com `chunk_id` nullable (já é FK ON DELETE CASCADE — revisar comportamento)

---

## US-003 — Estabilizar migrations SQL (PB-003)

**As** Tech Lead (P2),
**I want** migrations SQL aplicadas em ordem determinística e rastreadas,
**so that** setup funciona igual em qualquer máquina.

### Critérios de aceitação
- **Given** três migrations: `000_baseline.sql`, `010_compat.sql`, `020_hybrid_search.sql`
- **When** rodo `ai-memory setup` em banco novo
- **Then** migrations aplicadas em ordem numérica crescente
- **And** tabela `ai_schema_migrations` registra cada migration com hash
- **When** rodo setup novamente
- **Then** migrations já aplicadas são puladas
- **And** migration com hash diferente do registrado falha com mensagem clara

### Notas técnicas
- Renomear `001_create_schema.sql` -> `010_compat.sql` e `001_hybrid_search.sql` -> `020_hybrid_search.sql`
- `EnsureSchemaMigrationsTableAsync` já existe em `SetupCommand.cs:449` — usar hash + ordem explícita

---

## US-004 — Alinhar `MaxChunkLength` (PB-004)

**As** Ernane (P1),
**I want** tamanho máximo de chunk consistente entre código e docs,
**so that** embeddings têm qualidade previsível.

### Critérios de aceitação
- **Given** código em `ChunkingService.cs:11` com `MaxChunkLength = 1000`
- **When** unifico para `6000` (valor do README)
- **Then** threshold C# `8_000` revisado (ou removido se redundante)
- **And** README atualizado para refletir o valor final
- **And** teste unitário verifica que chunks > max são divididos

---

## US-005 — Guard stdout no MCP (PB-005)

**As** Agente IA (P3),
**I want** o servidor MCP não corrompa o stream JSON-RPC com logs,
**so that** minhas chamadas não quebram por causa de stdout sujo.

### Critérios de aceitação
- **Given** servidor MCP rodando
- **When** um serviço chamado por tool tenta `Console.WriteLine`
- **Then** saída redirecionada para `TextWriter.Null` (ou stderr)
- **And** stream stdout só contém JSON-RPC válido
- **And** teste de integração verifica stdout limpo após várias chamadas

### Notas técnicas
- No `McpCommand.RunAsync`, setar `Console.SetOut(TextWriter.Null)` após iniciar loop
- Log vai para `Console.Error` (já é o padrão em vários lugares)

---

## US-006 — Testes de serviços core (PB-006)

**As** QA,
**I want** testes unitários e de integração cobrindo chunking, RRF, reranker, compressão, heurísticas,
**so that** refatorações são seguras.

### Critérios de aceitação
- **Given** novo projeto `tests/AiMemory.Tests/AiMemory.Tests.csproj`
- **When** executo `dotnet test`
- **Then** cobre: `ChunkingService` (Roslyn + fallback + SQL + MD), `RerankerService`, `ContextCompressionService`, `ContextualChunkingService`, `RuleExtractionService`, `KnowledgeExtractionService`, `TextNormalizationService`, `HashService`
- **And** testes de integração com Testcontainers/Postgres cobrem: `PgVectorService` upsert/search/RRF, migrations SQL idempotentes
- **And** cobertura de serviços core >= 70%
- **And** CI roda `dotnet test` (após PB-018)

---

## US-007 — Split `PgVectorService` (PB-007)

**As** Tech Lead,
**I want** `PgVectorService` dividido em repositórios focados,
**so that** código é testável e legível.

### Critérios de aceitação
- **Given** `PgVectorService.cs` com 1290 linhas
- **When** split em `ChunkRepository`, `RuleRepository`, `KnowledgeRepository`, `SymbolGraphRepository`, `SearchService`
- **Then** nenhuma classe excede 400 linhas
- **And** comportamento público preservado (sem breaking change na CLI/MCP)
- **And** testes de US-006 continuam verdes
- **And** predicates SQL (EF migration, test, etc.) movidos para `SqlPredicates` estática

---

## US-008 — Batch embedding no Ollama (PB-008)

**As** Ernane (P1),
**I want** indexação envie múltiplos chunks por request ao Ollama,
**so that** indexação completa em minutos ao invés de horas.

### Critérios de aceitação
- **Given** 1000 chunks para indexar
- **When** rodo `ai-memory index chunks`
- **Then** Ollama recebe batches de K chunks por request (`input: []` em `/api/embed`)
- **And** número de round-trips reduzido em ~10x
- **And** benchmark em repo de referência mostra speedup mensurável
- **And** erro em 1 chunk do batch não perde os demais (retries individuais)

---

## US-009 — Paralelizar extração (PB-009)

**As** Ernane (P1),
**I want** extração `rules`/`knowledge` use `Parallel.ForEachAsync` com `--parallelism`,
**so that** fase mais lenta aproveita múltiplos cores.

### Critérios de aceitação
- **Given** extração semântica com 5000 candidatos
- **When** rodo `ai-memory index rules --semantic --parallelism 4`
- **Then** até 4 chunks processados concorrentemente
- **And** `ai_extraction_chunk_state` atualizado sem race condition
- **And** speedup ~N× cores observado
- **And** progress panel funciona corretamente com concorrência

---

## US-010 — Retry/backoff Ollama (PB-010)

**As** Ernane (P1),
**I want** falhas transitórias do Ollama retentadas com backoff exponencial,
**so that** extração semântica longa não aborta por blip de rede.

### Critérios de aceitação
- **Given** Ollama retornando 503 temporariamente
- **When** `OllamaService.EmbedAsync` ou `GenerateJsonAsync` falha
- **Then** Polly retenta 3x com backoff exponencial + jitter
- **And** após 3 falhas, chunk marcado `failed` (não aborta todo o pipeline)
- **And** log de retry em stderr (não stdout no MCP)

---

## US-011 — Upgrade MCP `2025-11-25` (PB-011)

**As** Agente IA (P3),
**I want** servidor MCP fale a versão `2025-11-25`,
**so that** clients modernos negociem features novas.

### Critérios de aceitação
- **Given** `McpCommand.cs:10` com `ProtocolVersion = "2024-11-05"`
- **When** troco para `2025-11-25`
- **Then** handshake aceita versão do client (mantém lógica atual em `McpCommand.cs:138-145`)
- **And** `initialize` response anuncia `2025-11-25`
- **And** teste manual em Rider/Cursor/Claude Desktop confirma conexão
- **And** clients antigos que pedem `2024-11-05` continuam funcionando (version negotiation)

---

## US-012 — Tool Annotations (PB-012)

**As** Ernane (P1),
**I want** tools declaradas como `readOnlyHint: true`,
**so that** clientes pulam prompt de autorização em buscas só-leitura.

### Critérios de aceitação
- **Given** 7 tools: `search_code`, `search_business_rules`, `search_knowledge`, `find_related_files`, `get_symbol_callers`, `get_symbol_callees`, `get_class_hierarchy`
- **When** `CreateTools()` em `McpCommand.cs:352` adiciona `annotations = new { readOnlyHint = true }`
- **Then** Cursor/Claude Desktop pulam confirmação em tools só-leitura
- **And** nenhum tool escreve no repo (confirmação manual)

---

## US-013 — Structured Output (PB-013)

**As** Agente IA (P3),
**I want** resultados das tools em `structuredContent` tipado com `outputSchema`,
**so that** IDE renderiza cards/tabelas ao invés de JSON em texto.

### Critérios de aceitação
- **Given** `ToolJsonResult` serializa payload em `content[].text` hoje
- **When** adiciono `outputSchema` em cada tool e retorno `structuredContent`
- **Then** Rider renderiza resultado como card/tabela
- **And** `content[].text` ainda presente para clients antigos (backward compat)
- **And** schemas tipados para `CodeSearchResult`, `BusinessRuleSearchResult`, `KnowledgeSearchResult`, etc.

---

## US-014 — Ícones em tools (PB-014)

**As** Ernane (P1),
**I want** cada tool tenha ícone SVG,
**so that** visualmente identificável no IDE.

### Critérios de aceitação
- **Given** `CreateTools()` em `McpCommand.cs:352`
- **When** adiciono `icons = [{ src = "data:image/svg+xml;base64,..." }]`
- **Then** Rider/Cursor mostram ícone ao lado de cada tool
- **And** ícones embedados (sem asset externo)
- **And** ícones distintos: cérebro (search_*), grafo (get_symbol_*), arquivo (find_related_files)

---

## US-015 — Tool Execution Errors (PB-015)

**As** Agente IA (P3),
**I want** erros de validação de input viram Tool Execution Error (não Protocol Error),
**so that** posso me auto-corrigir tentando outros argumentos.

### Critérios de aceitação
- **Given** `McpCommand.cs:124` captura `ArgumentException` como tool error e `Exception` como protocol error
- **When** tool recebe argumento inválido
- **Then** resposta é `isError: true` em `content[]` (Tool Execution Error)
- **And** LLM vê o erro e tenta novamente com args diferentes
- **And** protocol errors (parse JSON, method not found) continuam como `-32600`/`-32601`

---

## US-016 — `title` field (PB-016)

**As** Ernane (P1),
**I want** tools tenham `title` humano além de `name` programático,
**so that** UI mostra "Search Code" ao invés de "search_code".

### Critérios de aceitação
- **Given** `CreateTools()` em `McpCommand.cs:352`
- **When** adiciono `title = "Search Code"` em cada tool
- **Then** Rider mostra título humano na lista de tools
- **And** `name` continua programático (`search_code`)

---

## US-017 — Tray split (PB-017)

**As** DevOps (P4),
**I want** tray separado em `AiMemory.Tray.csproj` com package próprio,
**so that** binário MCP é menor e releases desacoplados.

### Critérios de aceitação
- **Given** `ai-memory.csproj` com Avalonia embutida
- **When** crio `AiMemory.Tray.csproj` (PackageId `AiMemory.Tray`, ToolCommandName `ai-memory-tray`)
- **Then** `AiMemory.Tool` (CLI+MCP) não referencia Avalonia
- **And** `ai-memory tray install` instala `AiMemory.Tray` se faltar
- **And** binário MCP ~70% menor (medir antes/depois)
- **And** autostart aponta para `ai-memory-tray` (não `ai-memory` interno)

---

## US-018 — CI/CD (PB-018)

**As** DevOps (P4),
**I want** GitHub Actions faça build + test + pack em cada tag,
**so that** releases são reproduzíveis.

### Critérios de aceitação
- **Given** repositório sem workflows
- **When** crio `.github/workflows/ci.yml` e `release.yml`
- **Then** cada PR roda `dotnet build` + `dotnet test`
- **And** tag `vX.Y.Z` publica NuGet no feed configurado
- **And** release notes extraídos do CHANGELOG.md
- **And** build verde em Linux, macOS, Windows

---

## US-019 — Watcher real (PB-019)

**As** Ernane (P1),
**I want** `ai-memory watch` use `FileSystemWatcher` com debounce,
**so that** memória fica sempre fresca sem intervenção.

### Critérios de aceitação
- **Given** `WatchCommand.cs` hoje
- **When** implemento `FileSystemWatcher` + debounce 500ms
- **Then** arquivo `.cs` salvo reindexa só aquele arquivo
- **And** múltiplas salvagens em sequência não disparam N indexações (debounce)
- **And** renomeação/delete tratados (chunks órfãos limpos)
- **And** log mostra arquivo reindexado em tempo real

---

## US-020 — Heurísticas configuráveis (PB-020)

**As** Tech Lead de time .NET em inglês (P2),
**I want** patterns de regras de negócio em arquivo de configuração,
**so that** time em inglês não precisa editar SQL.

### Critérios de aceitação
- **Given** patterns pt-BR hard-coded em `PgVectorService.cs:9`
- **When** movo para `~/.aimemory/patterns.json` ou `config.json`
- **Then** usuário pode sobrescrever patterns sem recompilar
- **And** defaults continuam pt-BR (compatibilidade)
- **And** teste com patterns customizados passa

---

## US-021 — Cache LRU + TTL (PB-021)

**As** Ernane (P1),
**I want** cache de embedding MCP use LRU com TTL,
**so that** hit-rate maior em sessões longas.

### Critérios de aceitação
- **Given** `McpCommand.cs:343` com `Dictionary` FIFO
- **When** troco para `MemoryCache` (ou LRU custom) com TTL
- **Then** entries expiradas são removidas
- **And** hit-rate aumenta em sessões longas (benchmark)
- **And** thread-safe sem lock global

---

## US-022 a US-027 — MCP 2026-07-28 (futuro)

Rascunho — detalhar quando versão virar stable.

- **US-022 (PB-022)**: Upgrade MCP -> `2026-07-28` stable
- **US-023 (PB-023)**: `CacheableResult` (`ttlMs` + `cacheScope`)
- **US-024 (PB-024)**: `server/discover` (anunciar múltiplas versões)
- **US-025 (PB-025)**: Stateless (remover `initialize`)
- **US-026 (PB-026)**: MRTR (`input_required`)
- **US-027 (PB-027)**: `subscriptions/listen`

### Pré-requisitos para Fase 6
- Spec `2026-07-28` publicada como stable (não RC)
- Pelo menos 2 clients-alvo (Rider/Cursor/Claude Desktop/Codex) confirmam suporte
- ADR-002 revisado ou substituído
