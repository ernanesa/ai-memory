# Dívida Técnica

Registro de dívida técnica identificada. Cada item tem ID (`TD-XXX`), severidade, localização e plano de pagamento.

## Convenções

- **Severidade**: Alta (bloqueia evolução), Média (atrapalha mas contornável), Baixa (cosmética)
- **Juros**: aumenta com o tempo se não paga (refactors ficam mais caros)
- **Plano**: qual PB paga, ou "aceita" se débito assumido

## Dívida ativa

### TD-001 — Zero testes
- **Severidade**: Alta
- **Localização**: projeto inteiro
- **Descrição**: nenhum teste unitário ou de integração. Cobertura efetiva 0%.
- **Juros**: refactors estruturais (PB-007, PB-008, PB-009) são arriscados sem rede.
- **Pago por**: PB-006

### TD-002 — `PgVectorService` god class
- **Severidade**: Alta
- **Localização**: `Services/PgVectorService.cs` (1290 linhas)
- **Descrição**: predicates SQL, search, upsert, grafo, detecção tudo em uma classe.
- **Juros**: dificulta testar, ler, manter.
- **Pago por**: PB-007

### TD-003 — Chunks órfãos
- **Severidade**: Alta
- **Localização**: `PgVectorService.UpsertChunkAsync` (sem DELETE de órfãos)
- **Descrição**: arquivos deletados ficam no banco; busca retorna código fantasma.
- **Juros**: usuário perde confiança na busca.
- **Pago por**: PB-002

### TD-004 — Migrations SQL instáveis
- **Severidade**: Alta
- **Localização**: `sql/` (`001_*` duplicados), `SetupCommand.cs:436`
- **Descrição**: ordem por `OrderBy` lexical; `000` vs `001` se sobrepõem.
- **Juros**: setup não reproduzível entre máquinas.
- **Pago por**: PB-003

### TD-005 — Build quebrado em alguns ambientes
- **Severidade**: Alta
- **Localização**: `ConfigService.cs:300`
- **Descrição**: `Directory.SetUnixFileMode` não resolve no targeting pack net10.
- **Juros**: ninguém consegue buildar do zero em algumas máquinas.
- **Pago por**: PB-001

### TD-006 — Extração sequencial
- **Severidade**: Média
- **Localização**: `IndexCommand.cs:239`
- **Descrição**: `foreach` simples em vez de `Parallel.ForEachAsync`. Ignora `--parallelism`.
- **Juros**: indexação semântica dura horas em repos grandes.
- **Pago por**: PB-009

### TD-007 — Embedding sem batch
- **Severidade**: Média
- **Localização**: `OllamaService.EmbedAsync`
- **Descrição**: 1 chunk = 1 HTTP round-trip. Ollama suporta batch.
- **Juros**: indexação 10× mais lenta do que necessário.
- **Pago por**: PB-008

### TD-008 — MCP desatualizado
- **Severidade**: Média
- **Localização**: `McpCommand.cs:10` (`ProtocolVersion = "2024-11-05"`)
- **Descrição**: sem Tool Annotations, Structured Output, Tool Execution Errors.
- **Juros**: UX degradada em clients modernos; LLM para em erros recuperáveis.
- **Pago por**: PB-011 a PB-016

### TD-009 — Avalonia no projeto principal
- **Severidade**: Média
- **Localização**: `ai-memory.csproj` (PackageReference Avalonia)
- **Descrição**: CLI/MCP carrega assemblies de UI desnecessariamente.
- **Juros**: binário grande, startup lento, dep em headless/CI.
- **Pago por**: PB-017

### TD-010 — Sem retry no Ollama
- **Severidade**: Média
- **Localização**: `OllamaService`
- **Descrição**: falha transitória marca chunk `failed` permanentemente.
- **Juros**: extração semântica longa é frágil.
- **Pago por**: PB-010

### TD-011 — Stdout não guardado no MCP
- **Severidade**: Média
- **Localização**: `McpCommand.RunAsync`
- **Descrição**: qualquer `Console.WriteLine` corrompe stream JSON-RPC.
- **Juros**: bug latente que só manifesta em produção.
- **Pago por**: PB-005

### TD-012 — `watch` não é watcher real
- **Severidade**: Média
- **Localização**: `WatchCommand.cs`
- **Descrição**: roadmap admite. Provavelmente só re-roda `index` inteiro.
- **Juros**: memória fica desatualizada sem intervenção.
- **Pago por**: PB-019

### TD-013 — `MaxChunkLength` inconsistente
- **Severidade**: Baixa
- **Localização**: `ChunkingService.cs:11`, README, threshold C#
- **Descrição**: código 1000, README ~6000, C# 8000 — três valores diferentes.
- **Juros**: embeddings de qualidade imprevisível; doc desatualizada.
- **Pago por**: PB-004

### TD-014 — Heurísticas pt-BR hard-coded
- **Severidade**: Baixa
- **Localização**: `PgVectorService.cs:9` (predicates SQL)
- **Descrição**: patterns de regra de negócio em SQL fixo pt-BR.
- **Juros**: não generaliza para outros domínios/idiomas.
- **Pago por**: PB-020

### TD-015 — Cache FIFO
- **Severidade**: Baixa
- **Localização**: `McpCommand.cs:343`
- **Descrição**: evicta `Keys.First()` em vez de LRU. Sem TTL.
- **Juros**: hit-rate baixo em sessões longas.
- **Pago por**: PB-021

### TD-016 — Sem CI/CD
- **Severidade**: Média
- **Localização**: ausência de `.github/workflows/`
- **Descrição**: releases manuais, sem build em PR.
- **Juros**: regressões entram em main; releases não reproduzíveis.
- **Pago por**: PB-018

### TD-017 — Sem release tags
- **Severidade**: Baixa
- **Localização**: git tags
- **Descrição**: 16 commits, nenhuma tag estável.
- **Juros**: comunidade não sabe o que é estável.
- **Pago por**: PB-018 (CI/CD em tag)

### TD-018 — Branches `subagent-*` não limpas
- **Severidade**: Baixa
- **Localização**: `git branch -a`
- **Descrição**: 4 branches `subagent-*` já mergeadas ou obsoletas.
- **Juros**: poluição no `git branch`.
- **Pago por**: chore (não precisa PB — limpar direto)

## Dívida assumida (aceita)

Dívida que decidimos não pagar nesta fase. Documentada em ADRs.

### TD-A01 — Sem índice HNSW no pgvector
- **Decisão**: ADR-003
- **Trade-off**: busca O(n) em vez de O(log n). Aceitável para <100k chunks.

### TD-A02 — Ollama como backend de embedding (sem ONNX embutido)
- **Decisão**: ADR-003
- **Trade-off**: dependência externa, latência variável. Mantém flexibilidade de trocar modelo.

### TD-A03 — Sem cache persistente SQLite de embeddings
- **Decisão**: ADR-003
- **Trade-off**: recomputação em restart. Cache em memória FIFO/LRU basta por enquanto.

## Timeline de pagamento

| Sprint | Dívida paga |
|---|---|
| Sprint 1 | TD-003, TD-004, TD-005, TD-011, TD-013 |
| Sprint 2 | TD-001, TD-002 |
| Sprint 3 | TD-006, TD-007, TD-010 |
| Sprint 4 | TD-008, TD-009 |
| Sprint 5 | TD-012, TD-014, TD-015, TD-016, TD-017 |
