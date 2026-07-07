# Product Backlog

Itens priorizados por valor e risco. Ordem reflete prioridade — topo é mais importante.

## Convenções

- **ID**: `PB-XXX`
- **Fase**: 1 (Destravar), 2 (Confiança), 3 (Performance), 4 (MCP+Arquitetura), 5 (Maturidade), 6 (Futuro)
- **Prioridade**: P0 (bloqueante), P1 (alta), P2 (média), P3 (baixa)
- **Tamanho**: XS (~1h), S (~3h), M (~1 dia), L (~3 dias), XL (~1 semana)
- **Status**: To Do, In Progress, Review, Done, Blocked, Cancelled
- **Dor**: referência a `problem-statement.md` (DOR-XXX)

---

## Fase 1 — Destravar (Crítico)

| ID | Título | Fase | Prioridade | Tamanho | Status | Dor |
|---|---|---|---|---|---|---|
| PB-001 | Corrigir build quebrado (`SetUnixFileMode`) | 1 | P0 | XS | To Do | DOR-001 |
| PB-002 | Limpar chunks órfãos após indexação | 1 | P0 | S | To Do | DOR-002 |
| PB-003 | Estabilizar migrations SQL (renomear + tabela de migrations) | 1 | P0 | S | To Do | DOR-003 |
| PB-004 | Alinhar `MaxChunkLength` (código/README/threshold) | 1 | P1 | XS | To Do | DOR-013 |
| PB-005 | Guard stdout no MCP (`Console.Out` -> `TextWriter.Null`) | 1 | P1 | XS | To Do | DOR-011 |

## Fase 2 — Confiança

| ID | Título | Fase | Prioridade | Tamanho | Status | Dor |
|---|---|---|---|---|---|---|
| PB-006 | Adicionar testes (unitários + integração) | 2 | P1 | XL | To Do | DOR-004 |
| PB-007 | Split `PgVectorService` em repositórios | 2 | P1 | L | To Do | DOR-007 |

## Fase 3 — Performance/custo

| ID | Título | Fase | Prioridade | Tamanho | Status | Dor |
|---|---|---|---|---|---|---|
| PB-008 | Batch embedding no Ollama (`input: []`) | 3 | P1 | M | To Do | DOR-006 |
| PB-009 | Paralelizar extração `rules`/`knowledge` | 3 | P1 | M | To Do | DOR-005 |
| PB-010 | Retry/backoff no Ollama (Polly) | 3 | P2 | S | To Do | DOR-010 |

## Fase 4 — MCP upgrade + arquitetura

| ID | Título | Fase | Prioridade | Tamanho | Status | Dor |
|---|---|---|---|---|---|---|
| PB-011 | Upgrade MCP `2024-11-05` -> `2025-11-25` | 4 | P1 | S | To Do | DOR-009 |
| PB-012 | Tool Annotations (`readOnlyHint: true` nos 7 tools) | 4 | P1 | XS | To Do | DOR-009 |
| PB-013 | Structured Output com `outputSchema` | 4 | P1 | M | To Do | DOR-009 |
| PB-014 | Ícones em tools via `icons: [...]` | 4 | P2 | XS | To Do | DOR-009 |
| PB-015 | Tool Execution Errors separados de Protocol Errors | 4 | P1 | S | To Do | DOR-009 |
| PB-016 | `title` field em tools | 4 | P2 | XS | To Do | DOR-009 |
| PB-017 | Separar Tray em `AiMemory.Tray.csproj` + `AiMemory.Tool.csproj` | 4 | P2 | L | To Do | DOR-008 |

## Fase 5 — Maturidade

| ID | Título | Fase | Prioridade | Tamanho | Status | Dor |
|---|---|---|---|---|---|---|
| PB-018 | CI/CD (GitHub Actions: build + test + pack em tag) | 5 | P1 | M | To Do | DOR-016 |
| PB-019 | Watcher real com `FileSystemWatcher` + debounce 500ms | 5 | P2 | M | To Do | DOR-012 |
| PB-020 | Heurísticas pt-BR configuráveis (arquivo de patterns) | 5 | P3 | S | To Do | DOR-014 |
| PB-021 | Cache embedding MCP FIFO -> LRU com TTL | 5 | P3 | S | To Do | DOR-015 |

## Fase 6 — Futuro (quando MCP `2026-07-28` virar stable + clients suportarem)

| ID | Título | Fase | Prioridade | Tamanho | Status | Dor |
|---|---|---|---|---|---|---|
| PB-022 | Upgrade MCP -> `2026-07-28` stable | 6 | P3 | M | To Do | — |
| PB-023 | `CacheableResult` (`ttlMs` + `cacheScope`) | 6 | P3 | S | To Do | — |
| PB-024 | `server/discover` (anunciar múltiplas versões) | 6 | P3 | S | To Do | — |
| PB-025 | Stateless (remover `initialize`/`notifications/initialized`) | 6 | P3 | M | To Do | — |
| PB-026 | MRTR (`input_required`) para queries ambíguas | 6 | P3 | M | To Do | — |
| PB-027 | `subscriptions/listen` (IDE reage a `toolsListChanged`) | 6 | P3 | L | To Do | — |

---

## Dependências

- PB-006 (testes) deve vir antes de PB-007 (split) para ter rede de segurança
- PB-007 facilita PB-002, PB-008, PB-009 (testabilidade)
- PB-011 (upgrade MCP) é pré-requisito de PB-012 a PB-016
- PB-017 (tray split) independente de PB-011 a PB-016
- PB-022 é pré-requisito de PB-023 a PB-027
- PB-018 (CI/CD) deve vir antes do primeiro release público estável

## Itens explicitmente fora do escopo (anti-backlog)

- ONNX Runtime embutido (ver ADR-003)
- Índice HNSW no pgvector (ver ADR-003)
- Cache persistente SQLite de embeddings (ver ADR-003)
- Suporte a TypeScript/Python/Java (foco .NET)
- OAuth/auth no MCP (uso local)
- UI web além do dashboard existente
- Streaming de MCP via HTTP (continuamos em STDIO)
