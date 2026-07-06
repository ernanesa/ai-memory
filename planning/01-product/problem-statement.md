# Problemas atuais (Problem Statement)

Lista priorizada de dores identificadas na análise do projeto em 06/Jul/2026.

## P0 — Bloqueantes

### DOR-001 Build quebrado neste ambiente
- **Sintoma**: `error CS0117: "Directory" não contém uma definição para "SetUnixFileMode"` em `Configuration/ConfigService.cs:300`.
- **Causa**: targeting pack do net10.0 não expõe a API neste SDK, apesar do guard `OperatingSystem.IsWindows()` em runtime.
- **Impacto**: ninguém consegue buildar o projeto do zero neste ambiente. `EVOLUTION_REPORT.md` afirma "0 erros" mas não compila.
- **Evidência**: `dotnet build` falha com 1 erro.

### DOR-002 Chunks órfãos não são limpos
- **Sintoma**: `UpsertChunkAsync` upserta por `(project_id, file_path, content_hash)`, mas arquivos **deletados** do disco ficam no banco.
- **Causa**: só EF/test chunks são removidos (`DeleteEntityFrameworkMigrationChunksAsync`, `DeleteTestChunksAsync`); não há DELETE para arquivos removidos.
- **Impacto**: busca retorna código que não existe mais — agente IA responde com base em fonte fantasma. Bug silencioso.
- **Evidência**: `PgVectorService.cs:129` (upsert sem delete de órfãos).

### DOR-003 SQL migrations duplicadas/instáveis
- **Sintoma**: `001_create_schema.sql` e `001_hybrid_search.sql` com **mesmo prefixo `001`**, aplicados por `OrderBy` lexical em `SetupCommand.cs:436`.
- **Causa**: `000_schema.sql` (baseline) e `001_create_schema.sql` (compat) se sobrepõem em extensões/colunas. Funciona por sorte lexical ("create" < "hybrid").
- **Impacto**: setup não determinístico entre máquinas; mudança de nome quebra ordem.
- **Evidência**: `sql/` com 3 arquivos, dois com prefixo `001`.

## P1 — Altas

### DOR-004 Zero testes
- **Sintoma**: chunking, RRF, reranker, compressão, heurísticas de extração — nada tem cobertura.
- **Impacto**: bug em RRF ou compressão = resposta errada da IA sem alerta. Refatorar `PgVectorService` é arriscado.
- **Evidência**: nenhum projeto de teste, `dotnet test` não existe.

### DOR-005 Extração `rules`/`knowledge` sequencial
- **Sintoma**: `IndexCommand.cs:239` é `foreach` simples. Cada candidato gera embedding (+ LLM no semântico). Fase mais lenta e **ignora `--parallelism`**.
- **Impacto**: indexação semântica de repositórios grandes dura horas desnecessariamente.

### DOR-006 Ollama embedding sem batch
- **Sintoma**: `OllamaService.EmbedAsync` faz 1 chunk = 1 HTTP round-trip. Ollama suporta `input: []` em `/api/embed`.
- **Impacto**: indexação tem 10× mais round-trips do que necessário.

### DOR-007 `PgVectorService` god class (1290 linhas)
- **Sintoma**: predicates SQL, search, upsert, grafo, detecção de teste/migration tudo em uma classe.
- **Impacto**: ilegível, não testável, difícil de manter.

### DOR-008 Avalonia no projeto principal
- **Sintoma**: cada `ai-memory mcp`/`index` carrega assemblies Avalonia.
- **Impacto**: startup mais lento, binário maior, dep desnecessária em headless/CI.

### DOR-009 MCP desatualizado (`2024-11-05`)
- **Sintoma**: `McpCommand.cs:10` hard-coda primeira versão estável, ~20 meses atrás.
- **Impacto**: sem Tool Annotations (clientes pedem autorização a cada chamada), sem Structured Output, sem ícones, sem Tool Execution Errors (LLM para em erro de input).

### DOR-010 Sem retry/backoff no Ollama
- **Sintoma**: falha transitória (carga/timeout) marca chunk como `failed` permanentemente até próximo `index`.
- **Impacto**: extração semântica longa é frágil; falhas de rede exigem re-rodar tudo.

### DOR-011 Guard stdout no MCP ausente
- **Sintoma**: MCP usa stdout como canal JSON-RPC. Qualquer `Console.WriteLine` em serviço chamado corrompe o stream.
- **Impacto**: bug latente — só manifesta quando algum log vaza para stdout.

## P2 — Médias

### DOR-012 `watch` não é watcher real
- **Sintoma**: roadmap do README admite. Hoje deve só re-rodar `index`.
- **Impacto**: memória fica desatualizada sem intervenção manual.

### DOR-013 `MaxChunkLength` inconsistente
- **Sintoma**: código `MaxChunkLength = 1000` em `ChunkingService.cs:11`; README diz "~6000"; threshold C# `8_000` em outra unidade.
- **Impacto**: chunks de tamanho não coerente; embeddings de qualidade imprevisível; doc desatualizada.

### DOR-014 Heurísticas pt-BR hard-coded
- **Sintoma**: `não pode`, `bloqueado`, `vencido`, `Elegivel` em SQL (`PgVectorService.cs:9`).
- **Impacto**: travado em domínio .NET lusófono; não generaliza.

### DOR-015 Cache embedding MCP FIFO, não LRU
- **Sintoma**: `McpCommand.cs:343` evicta `Keys.First()`. Sem TTL, `lock` global.
- **Impacto**: hit-rate baixo em sessões longas.

### DOR-016 Sem CI/CD
- **Sintoma**: nenhum workflow; NuGet empacotado manualmente.
- **Impacto**: releases não reproduzíveis; regressões entram em main.

## P3 — Baixas

### DOR-017 Sem release tags no GitHub
- **Sintoma**: 16 commits, nenhuma tag estável.
- **Impacto**: comunidade não sabe o que é estável.

### DOR-018 Documentação divergente do código
- **Sintoma**: README descreve chunks de 6000 chars; código usa 1000.
- **Impacto**: usuários confundem-se com configuração.

---

## Mapa de dores para backlog

| Dor | Backlog item |
|---|---|
| DOR-001 | PB-001 |
| DOR-002 | PB-002 |
| DOR-003 | PB-003 |
| DOR-013 | PB-004 |
| DOR-011 | PB-005 |
| DOR-004 | PB-006 |
| DOR-007 | PB-007 |
| DOR-006 | PB-008 |
| DOR-005 | PB-009 |
| DOR-010 | PB-010 |
| DOR-009 | PB-011 |
| DOR-009 (anotações) | PB-012 |
| DOR-009 (estruturado) | PB-013 |
| DOR-009 (ícones) | PB-014 |
| DOR-009 (erros) | PB-015 |
| DOR-009 (títulos) | PB-016 |
| DOR-008 | PB-017 |
| DOR-016 | PB-018 |
| DOR-012 | PB-019 |
| DOR-014 | PB-020 |
| DOR-015 | PB-021 |
