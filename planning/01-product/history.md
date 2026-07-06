# Histórico do projeto

## Linha do tempo (git log)

| Commit | Tipo | Descrição |
|---|---|---|
| `ef63796` | initial | Commit inicial |
| `e01df3e` | fix | Corrige problemas de indexação |
| `89a0661` | feat | Implementa funcionalidade MCP |
| `692f013` | docs | Melhora prompt de análise |
| `fbe6294` | refactor | Utiliza guid para ids no banco e adiciona registro de workspaces |
| `e9a8b9f` | feat | Adiciona dashboard |
| `b241edd` | feat | Melhora confiabilidade de knowledge e business rules |
| `8281da4` | feat | Indexação de knowledge e business rules |
| `78ba88b` | feat | Utiliza o roslyn parser na indexação de arquivos `.cs` |
| `4c42319` | feat | Otimizações de performace e exclusão de tipos de arquivos |
| `d1ad904` | docs | Melhora documentação |
| `9e6fa85` | feat | Adiciona suporte a linux e windows |
| `4bcc77c` | feat | Permite usar banco já existente |
| `305a407` | refactor | Consolida tray no projeto principal e detecta GUI automaticamente |
| `9f20bec` | refactor | Consolida tray no projeto principal e detecta GUI automaticamente (dup) |
| `acb9c80` | feat | System tray setup e metadata updates |
| `7c11e4b` | feat | Paralelismo e caching na indexação/busca |

## Branches ativas

- `main` — branch principal
- `subagent-Program-cs-Tray-Setup-Adder-self-e27fcaac`
- `subagent-README-Comprehensive-Updater-self-93ca5636`
- `subagent-TraySetupService-macOS-Fixer-self-09e58e55`
- `subagent-csproj-NuGet-Metadata-Updater-self-fe581762`

> Branches `subagent-*` são de trabalho de subagentes opencode. Devem ser limpas quando mergeadas.

## Evolução arquitetural (resumo do `EVOLUTION_REPORT.md`)

O relatório de evolução documentou 5 eixos de refactor:

1. **Separação de responsabilidades (.NET only)** — chunker limpo de linguagens não-.NET; suporte a `.razor`/`.cshtml`; de-inlining de `IndexCommand.cs` (2400 -> 544 linhas) em `TextNormalizationService`, `RuleExtractionService`, `KnowledgeExtractionService`.
2. **Compressão de contexto inteligente (estilo Netflix Headroom)** — `ContextCompressionService`: colapso de `using`, remoção de licenças, contração de corpos de métodos não-alvo (`/* body omitted */`).
3. **Contextual chunking (ancoragem semântica)** — `ContextualChunkingService`: prefixo `[CONTEXT: Project | File | Symbol | ChunkType]` no embedding, mas não no banco.
4. **Code Graph RAG (AST Roslyn)** — tabelas `ai_symbols` + `ai_symbol_relations`; `SymbolGraphService`; MCP tools `get_symbol_callers`, `get_symbol_callees`, `get_class_hierarchy`.
5. **Busca híbrida (RRF) + reranker heurístico** — índices GIN + triggers `tsvector` em `001_hybrid_search.sql`; RRF (`1/(60+rank)`) no `PgVectorService`; `RerankerService`.

## Evolução do Tray (também do `EVOLUTION_REPORT.md`)

- Correções de handle leak em `Process.GetProcesses()`
- Desbloqueio de UI thread com `Task.Run`
- Prevenção de deadlocks de pipe
- Ícone com canal alfa transparente + autocrop 256x256
- Submenus: workspace switcher, controle PostgreSQL, inicializador de banco/schema
- Correção de collation mismatch do Postgres

## Estado herdado que motivou este planejamento

Apesar da maturidade arquitetural (RRF, contextual chunking, grafo, compressão), o projeto acumulou dívida operacional:
- Build quebra em alguns ambientes
- Zero testes
- Migrations instáveis
- Extração lenta (sequencial, sem batch)
- MCP em versão antiga
- Tray misturado no package principal

Este planejamento ataca essa dívida **sem** reescrever a arquitetura — apenas estabiliza, otimiza e atualiza o que já funciona bem.
