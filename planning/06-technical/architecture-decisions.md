# Architecture Decision Records (ADRs)

Decisões técnicas com contexto, alternativas e consequências. Cada ADR é imutável após publicado — supersede por novo ADR com link.

## Convenção

- **ID**: `ADR-XXX`
- **Status**: Proposed, Accepted, Deprecated, Superseded by ADR-YYY
- **Data**: AAAA-MM-DD

## Lista de ADRs

- [ADR-001](#adr-001-upgrade-mcp-para-2025-11-25-não-2025-06-18) — Upgrade MCP para `2025-11-25` (não `2025-06-18`)
- [ADR-002](#adr-002-não-adotar-mcp-2026-07-28-rc-até-virar-stable) — Não adotar MCP `2026-07-28` RC até virar stable
- [ADR-003](#adr-003-postergar-otimização-de-latência-onnx-hnsw-cache-persistente) — Postergar otimização de latência (ONNX, HNSW, cache persistente)
- [ADR-004](#adr-004-separar-tray-em-projeto-package-próprio) — Separar Tray em projeto/package próprio
- [ADR-005](#adr-005-manter-ollama-como-backend-de-embedding) — Manter Ollama como backend de embedding
- [ADR-006](#adr-006-testcontainers-para-testes-de-integração) — Testcontainers para testes de integração
- [ADR-007](#adr-007-polly-para-retrybackoff) — Polly para retry/backoff
- [ADR-008](#adr-008-migrations-sql-com-tabela-ai_schema_migrations-e-ordem-numérica) — Migrations SQL com tabela `ai_schema_migrations` e ordem numérica

---

## ADR-001: Upgrade MCP para `2025-11-25` (não `2025-06-18`)

- **Status**: Accepted
- **Data**: 2026-07-06

### Contexto
O ai-memory usa MCP `2024-11-05` (primeira estável, ~20 meses atrás). Precisamos adotar Tool Annotations, Structured Output, Tool Execution Errors, ícones, `title` field.

### Decisão
Fazer upgrade direto para `2025-11-25` (Latest stable no GitHub em 06/Jul/2026), **não** parar em `2025-06-18`.

### Alternativas consideradas
1. **`2025-06-18`** (sugerido inicialmente): traz Tool Annotations e Structured Output, mas perde ícones, Tasks experimental, Tool Execution Errors refinement, JSON Schema 2020-12 como padrão.
2. **`2025-11-25`** (escolhida): Latest stable, não quebra nada de `2025-06-18` (só adiciona).
3. **`2026-07-28` RC**: ver ADR-002 — não adotar ainda.

### Justificativa
MCP tem version negotiation embutido no handshake. Subir para a Latest stable **não corta clients antigos** — eles pedem a versão que suportam e o server responde ack. Não há "risco de compat" em pular uma estável; só há custo de não aproveitar features da última.

### Consequências
- **Positivas**: ícones, Tasks experimental, Tool Execution Errors refinados, JSON Schema 2020-12 completo.
- **Negativas**: spec maior, mais coisas para implementar corretamente.
- **Risco**: clients muito antigos que só falam `2024-11-05` — mitigado por version negotiation.

### Relacionado
- PB-011 a PB-016
- Implementação em Sprint 4

---

## ADR-002: Não adotar MCP `2026-07-28` RC até virar stable

- **Status**: Accepted
- **Data**: 2026-07-06

### Contexto
Existe RC `2026-07-28` (cut 29/Mai/2026, spec em `draft`). Traz `CacheableResult`, `server/discover`, stateless, MRTR, `subscriptions/listen`. É uma revisão grande com quebras semânticas.

### Decisão
Não adotar `2026-07-28` nesta fase. Só avaliar quando (a) virar stable (~Jul/2026), **e** (b) pelo menos 2 clients-alvo (Rider/Cursor/Claude Desktop/Codex) confirmarem suporte.

### Alternativas consideradas
1. **Adotar RC agora**: ganha `CacheableResult` (economia de tokens no prompt), mas arrisca retrabalho (spec pode mudar) e clients podem não suportar.
2. **Nunca adotar**: perde ganhos estruturais do stateless e CacheableResult.
3. **Preparar terreno sem comprometer** (escolhida): estruturar `ToolJsonResult` e `CreateTools()` já pensando em `CacheableResult` (ordem determinística — já é array fixo) e `outputSchema` (tipar results). Forward-compatible sem depender do RC.

### Justificativa
- Release note do RC alerta: "this specification is not final. Changes may be introduced between the RC and the final release."
- SDKs adotam no próprio ritmo — clients podem não suportar ao mesmo tempo.
- Custo de implementação não trivial (remover `initialize`, `server/discover`, `resultType` em todos os results, `CacheableResult`, `subscriptions/listen`).

### Consequências
- **Positivas**: estabilidade, sem retrabalho, sem risco de cliente incompatível.
- **Negativas**: adiar ganho de `CacheableResult` (economia de ~1.4k tokens/sessão no cache do client).
- **Reavaliação**: a cada release MCP stable.

### Relacionado
- PB-022 a PB-027 (Fase 6, futuro)
- ADR-001 (upgrade para `2025-11-25` cobre 80% do benefício imediato)

---

## ADR-003: Postergar otimização de latência (ONNX, HNSW, cache persistente)

- **Status**: Accepted
- **Data**: 2026-07-06

### Contexto
Análise de latência de `search_code` mostrou gargalos em: embedding Ollama (50–200 ms), query RRF no Postgres (5–30 ms). Três otimizações identificadas:
1. ONNX Runtime embutido (eliminar hop HTTP para Ollama)
2. Índice HNSW no pgvector (busca O(log n))
3. Cache persistente SQLite de embeddings

### Decisão
Postergar as três otimizações para fase futura. Focar esta fase em estabilização, testes, performance de indexação (batch + paralelismo) e upgrade MCP.

### Alternativas consideradas
1. **Adotar as três agora**: latência cai para <10 ms, mas adiciona 3 dependências/binários grandes, RAM total sobe, pipeline de release de modelo novo.
2. **Adotar só HNSW** (menor risco): SQL aditivo, ganho imediato em repos grandes. Mas continua dependendo de Ollama para embedding.
3. **Postergar todas** (escolhida): foco em dívida operacional acumulada (testes, build, migrations, MCP desatualizado).

### Justificativa
- Latência atual (60–230 ms) é aceitável para uso interativo em IDEs.
- Dívida operacional (build quebrado, zero testes, migrations instáveis) é mais urgente que otimização de performance.
- Otimização de indexação (PB-008 batch, PB-009 paralelismo) tem ganho maior e menor risco.
- Upgrade MCP (PB-011 a PB-016) entrega benefício de UX imediato.

### Consequências
- **Positivas**: foco em dívida urgente, menor risco, menos dependências.
- **Negativas**: latência de busca permanece 60–230 ms. Repos >100k chunks podem ter performance degradada sem HNSW.
- **Reavaliação**: após v0.5.0, se adoção crescer e latência virar reclamação, reabrir.

### Relacionado
- TD-A01, TD-A02, TD-A03 (dívida assumida)
- Fase 6 (futuro) pode incluir estas otimizações

---

## ADR-004: Separar Tray em projeto/package próprio

- **Status**: Accepted
- **Data**: 2026-07-06

### Contexto
`ai-memory.csproj` referencia Avalonia e empacota tray no mesmo package NuGet. Cada chamada `ai-memory mcp` ou `ai-memory index` carrega assemblies Avalonia.

### Decisão
Separar em dois projetos:
- `ai-memory.csproj` -> `AiMemory.Tool` (CLI + MCP, sem Avalonia)
- `AiMemory.Tray.csproj` -> `AiMemory.Tray` (tray, com Avalonia, comando `ai-memory-tray`)

Instalação simultânea via `ai-memory tray install` (detecta falta de `AiMemory.Tray` e roda `dotnet tool install -g AiMemory.Tray`).

### Alternativas consideradas
1. **Manter status quo** (tray embutido): binário grande, dep em headless/CI, releases acoplados.
2. **Um package, dois executáveis** (tray como self-contained dentro de `tools/`): dobra tamanho do nupkg, toda instalação carrega tray.
3. **Dois projetos, dois packages** (escolhida): clean, releases desacoplados, headless instala só CLI.

### Justificativa
- Tray era projeto separado antes — foi consolidado em `305a407`. Reverter para separado.
- Usuários server/CI não precisam de tray.
- Upgrade de ícone do tray não deveria repacotear a CLI.
- Binário MCP ~70% menor, startup mais rápido.

### Consequências
- **Positivas**: binário MCP leve, releases desacoplados, separação de responsabilidades.
- **Negativas**: usuários existentes precisam rodar `ai-memory tray update` para re-criar autostart. Pipeline de release publica 2 nupkgs.
- **Risco**: RS-006 (autostart quebra) — mitigado por `tray update` e release notes.

### Relacionado
- PB-017
- Sprint 4

---

## ADR-005: Manter Ollama como backend de embedding

- **Status**: Accepted
- **Data**: 2026-07-06

### Contexto
Ollama é usado para embedding (bge-m3) e extração semântica (qwen2.5-coder:7b). Alternativa: embutir ONNX Runtime e eliminar Ollama para embedding (ver ADR-003).

### Decisão
Manter Ollama como backend nesta fase. Não embutir ONNX.

### Alternativas consideradas
1. **ONNX embutido**: remove dependência Ollama para embedding, fixa versão do modelo, viabiliza CI sem Ollama. Mas RAM do processo MCP sobe ~1GB, pipeline de release de modelo novo.
2. **Manter Ollama** (escolhida): flexível (trocar modelo com 1 linha), comunidade grande, já funciona.
3. **Híbrido**: ONNX para embedding, Ollama só para semântica. Mais complexo.

### Justificativa
- Para um projeto que ainda tunaa embeddings, Ollama é mais flexível.
- Eliminar Ollama para embedding não elimina para extração semântica — dependência permanece.
- Setup com 3 dependências (Postgres + Ollama + modelos) é aceitável por enquanto.

### Consequências
- **Positivas**: flexibilidade, sem custo de distribuição de modelo.
- **Negativas**: cold start de Ollama (~3-10s na primeira chamada), versão de modelo pode mudar silenciosamente.
- **Reavaliação**: se adoção crescer e setup complexo virar barreira (RS-010), reabrir.

### Relacionado
- ADR-003
- TD-A02

---

## ADR-006: Testcontainers para testes de integração

- **Status**: Accepted
- **Data**: 2026-07-06

### Contexto
`PgVectorService` faz queries SQL complexas (RRF, predicates, joins). Testar com mock é frágil — não valida SQL. Precisamos de Postgres real + pgvector nos testes.

### Decisão
Usar `Testcontainers.PostgreSql` para testes de integração. Cada teste sobe um container Postgres+pgvector, aplica migrations, roda queries, desce container.

### Alternativas consideradas
1. **Postgres compartilhado em CI**: frágil, paralelismo difícil, estado vaza entre testes.
2. **Mock Npgsql**: não valida SQL; falso positivo.
3. **Testcontainers** (escolhida): isolado, real, paralelizável, limpa entre testes.
4. **Embedded Postgres** (e.g.,EmbeddedPostgres): menos infra, mas pgvector nem sempre disponível.

### Justificativa
- Testcontainers é padrão .NET para testes de integração com DB.
- Docker já disponível em GitHub Actions.
- pgvector precisa ser extensão real no Postgres — Testcontainers suporta.

### Consequências
- **Positivas**: testes de integração reais, confiança para refatorar SQL.
- **Negativas**: testes mais lentos (subir container ~5s por suite), exige Docker local e em CI.

### Relacionado
- PB-006
- Sprint 2

---

## ADR-007: Polly para retry/backoff

- **Status**: Accepted
- **Data**: 2026-07-06

### Contexto
`OllamaService` falha em blips de rede/carga. Hoje, falha transitória marca chunk `failed` permanentemente.

### Decisão
Adicionar `Polly` v8 com retry policy em `OllamaService`: 3 retentativas, backoff exponencial + jitter.

### Alternativas consideradas
1. **Retry manual com `for`**: funciona, mas reinventar jitter é propenso a erro.
2. **Polly v7**: API mais antiga, v8 é atual.
3. **Polly v8** (escolhida): padrão .NET, API moderna, jitter embutido.
4. **Microsoft.Extensions.Http.Resilience**: mais pesado, voltado para `IHttpClientFactory`.

### Justificativa
- Polly é biblioteca padrão .NET para resiliência.
- v8 tem API fluente moderna e jitter integrado.
- Aplica a `EmbedAsync`, `EmbedBatchAsync`, `GenerateJsonAsync`.

### Consequências
- **Positivas**: resiliência em extração semântica longa.
- **Negativas**: mais uma dependência (~200KB).

### Relacionado
- PB-010
- Sprint 3

---

## ADR-008: Migrations SQL com tabela `ai_schema_migrations` e ordem numérica

- **Status**: Accepted
- **Data**: 2026-07-06

### Contexto
Hoje `sql/` tem `000_schema.sql`, `001_create_schema.sql`, `001_hybrid_search.sql` — dois com mesmo prefixo `001`. Aplicados por `OrderBy` lexical em `SetupCommand.cs:436`. Funciona por sorte ("create" < "hybrid").

### Decisão
1. Renomear para ordem numérica explícita: `000_baseline.sql`, `010_compat.sql`, `020_hybrid_search.sql`.
2. Usar tabela `ai_schema_migrations` (já existe `EnsureSchemaMigrationsTableAsync` em `SetupCommand.cs:449`) com nome da migration + hash SHA256 do conteúdo.
3. Aplicar em ordem numérica crescente; pular já aplicadas; falhar se hash divergir.

### Alternativas consideradas
1. **Ferramenta externa (Flyway, DbUp)**: adiciona dependência, configuração extra.
2. **Manter `OrderBy` lexical com nomes que ordenam certo**: frágil, qualquer renomeação quebra.
3. **Tabela interna com hash + ordem numérica** (escolhida): self-contained, determinístico, detecta mudança de migration aplicada.

### Justificativa
- Setup deve ser reproduzível entre máquinas.
- Hash detecta se migration já aplicada foi alterada (perigo silencioso).
- Ordem numérica explícita é à prova de renomeação.

### Consequências
- **Positivas**: setup determinístico, detecção de migration alterada.
- **Negativas**: mudar migration já aplicada exige bump de número (não edita migration antiga).

### Relacionado
- PB-003
- Sprint 1
- RS-002 (migration quebra bancos existentes)
