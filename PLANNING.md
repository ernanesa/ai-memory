# PLANNING — ai-memory

Documento mestre de planejamento Scrum do projeto **ai-memory**. Cobertura completa: visão, personas, backlog, sprints, releases, dívida técnica, riscos, ADRs e retrospectivas.

> Os artefatos completos vivem em `planning/`. Este arquivo é o índice de navegação rápida.

---

## Status atual (snapshot)

| Dimensão | Estado | Versão |
|---|---|---|
| Build | Quebrado neste ambiente (`Directory.SetUnixFileMode` não resolve no targeting pack net10) | — |
| Versão MCP | `2024-11-05` (primeira estável, ~20 meses atrás) | — |
| Versão package | `0.1.5` | `ai-memory.csproj` |
| Cobertura de testes | 0% | — |
| Pipeline CI/CD | Inexistente | — |
| Latência típica `search_code` | 60–230 ms (gargalo: embedding Ollama) | — |
| Releases no GitHub | 16 commits, sem tags estáveis | — |

---

## Visão do produto (resumo)

Memória local de engenharia para agentes de IA. Indexa código, regras de negócio e conhecimento arquitetural de múltiplos projetos .NET e expõe tudo via MCP (Model Context Protocol) sobre stdio para IDEs (Rider, VS Code, Cursor, Codex).

Stack: **.NET 10 + Ollama (bge-m3) + PostgreSQL + pgvector + Avalonia (tray)**.

### Objetivos de produto
- Reduzir tokens enviados ao LLM em sessões de engenharia
- Indexar múltiplos workspaces/projetos
- Extrair regras de negócio e conhecimento técnico
- Integrar Rider, VS Code, Codex por MCP
- Funcionar 100% local, sem nuvem

### Não-objetitos desta fase
- Otimização de latência via ONNX embutido / HNSW / cache persistente (postergada — ver ADR-003)
- Adição de novas linguagens (foco .NET/C#)
- Multi-tenant / nuvem

---

## Navegação rápida

| Quero... | Onde olhar |
|---|---|
| Visão, personas, problema, história | [`planning/01-product/`](planning/01-product/) |
| Processo Scrum (papéis, DoR, DoD, cerimônias) | [`planning/02-process/`](planning/02-process/) |
| Backlog completo, épics, user stories, dívida, riscos | [`planning/03-backlog/`](planning/03-backlog/) |
| Planos de sprint (1 a 5) | [`planning/04-sprints/`](planning/04-sprints/) |
| Roadmap de releases e template de release notes | [`planning/05-releases/`](planning/05-releases/) |
| ADRs e restrições técnicas | [`planning/06-technical/`](planning/06-technical/) |
| Templates e retrospectivas | [`planning/07-retrospectives/`](planning/07-retrospectives/) |

---

## Backlog priorizado (top-level)

### Fase 1 — Destravar (Crítico)
- PB-001 Corrigir build quebrado
- PB-002 Limpar chunks órfãos
- PB-003 Estabilizar migrations SQL
- PB-004 Alinhar `MaxChunkLength`
- PB-005 Guard stdout no MCP

### Fase 2 — Confiança
- PB-006 Adicionar testes (unitários + integração com Testcontainers)
- PB-007 Split `PgVectorService` em repositórios

### Fase 3 — Performance/custo
- PB-008 Batch embedding no Ollama
- PB-009 Paralelizar extração `rules`/`knowledge`
- PB-010 Retry/backoff no Ollama (Polly)

### Fase 4 — MCP upgrade + arquitetura
- PB-011 Upgrade MCP `2024-11-05` -> `2025-11-25`
- PB-012 Tool Annotations (`readOnlyHint: true` nos 7 tools)
- PB-013 Structured Output com `outputSchema`
- PB-014 Ícones em tools
- PB-015 Tool Execution Errors separados de Protocol Errors
- PB-016 `title` field em tools
- PB-017 Separar Tray em projeto/package próprio

### Fase 5 — Maturidade
- PB-018 CI/CD (GitHub Actions)
- PB-019 Watcher real com `FileSystemWatcher`
- PB-020 Heurísticas pt-BR configuráveis
- PB-021 Cache embedding MCP FIFO -> LRU com TTL

### Fase 6 — Futuro (quando `2026-07-28` MCP virar stable)
- PB-022 Upgrade MCP -> `2026-07-28`
- PB-023 `CacheableResult` (`ttlMs` + `cacheScope`)
- PB-024 `server/discover`
- PB-025 Stateless (remover `initialize`)
- PB-026 MRTR (`input_required`)
- PB-027 `subscriptions/listen`

Detalhe completo: [`planning/03-backlog/product-backlog.md`](planning/03-backlog/product-backlog.md)

---

## Roadmap de releases (resumo)

| Release | Conteúdo previsto | Sprint alvo |
|---|---|---|
| `v0.2.0` — Estabilização | Fase 1 + Fase 2 | Sprint 1 + Sprint 2 |
| `v0.3.0` — Performance | Fase 3 | Sprint 3 |
| `v0.4.0` — MCP 2025-11-25 + Tray split | Fase 4 | Sprint 4 |
| `v0.5.0` — Maturidade | Fase 5 | Sprint 5 |
| `v0.6.0+` — MCP 2026-07-28 | Fase 6 | Sprints futuros |

Detalhe: [`planning/05-releases/roadmap.md`](planning/05-releases/roadmap.md)

---

## Papéis Scrum assumidos

| Papel | Atribuição | Responsabilidade |
|---|---|---|
| Product Owner | Ernane (autor do projeto) | Visão, priorização, aceite |
| Scrum Master | Ernane (rotativa) | Facilitação, remoção de bloqueios, processo |
| Tech Lead / Arquiteto | Ernane | ADRs, decisões técnicas, code review |
| Dev Team | Ernane + subagentes opencode | Implementação |
| QA | Ernane + testes automatizados | Qualidade, cobertura |
| DevOps | Ernane | CI/CD, releases |
| Stakeholders | Comunidade .NET + agentes IA consumidores | Feedback |

Detalhe: [`planning/02-process/roles.md`](planning/02-process/roles.md)

---

## Como contribuir com este planejamento

1. Cada item do backlog tem ID único (`PB-XXX`).
2. Mudanças de prioridade refletem-se em `planning/03-backlog/product-backlog.md` e nos sprints correspondentes.
3. Decisões técnicas registradas como ADRs em `planning/06-technical/architecture-decisions.md`.
4. Retrospectivas ao final de cada sprint em `planning/07-retrospectives/`.
5. Atualizar este arquivo ao mudar status, versão ou roadmap.
