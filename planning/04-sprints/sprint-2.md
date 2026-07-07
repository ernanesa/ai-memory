# Sprint 2 — Confiança

- **Datas**: semana 2
- **Goal**: testes cobrindo serviços core; `PgVectorService` split em repositórios
- **Release alvo**: v0.2.0 (com Sprint 1)
- **Capacity**: ~20h/semana

## Sprint Backlog

| ID | Item | Tamanho | Prioridade | Status |
|---|---|---|---|---|
| PB-006 | Adicionar testes (unitários + integração) | XL | P1 | To Do |
| PB-007 | Split `PgVectorService` em repositórios | L | P1 | To Do |

**Total estimado**: ~30h (XL+L) — sprint apertado, considerar spill

## Sprint Goal

"Refatorar com confiança. Cobertura de testes >70% em serviços core. `PgVectorService` split sem regressão."

## Critérios de aceitação do sprint

- [ ] Projeto `tests/AiMemory.Tests/AiMemory.Tests.csproj` criado
- [ ] `dotnet test` verde
- [ ] Cobertura de serviços core >= 70%
- [ ] `PgVectorService` split em 5 classes, nenhuma >400 linhas
- [ ] Comportamento público preservado (CLI e MCP funcionam igual)
- [ ] Benchmark de busca antes/depois do split mostra mesmo resultado

## Tasks detalhadas

### PB-006 — Testes

#### Estrutura
- `tests/AiMemory.Tests/AiMemory.Tests.csproj` (xUnit + FluentAssertions)
- `tests/AiMemory.Tests/Unit/` — testes unitários
- `tests/AiMemory.Tests/Integration/` — Testcontainers/Postgres

#### Cobertura unitária (alvo)
- `ChunkingService`:
  - `ChunkCSharp` com Roslyn: classe pequena -> 1 chunk; classe grande -> chunks por método; fallback regex; arquivo vazio; arquivo só com usings
  - `ChunkSql`: procedure, view, function, bloco GO
  - `ChunkMarkdown`: seção, subseção, sem header
  - `SplitChunk`: limite respeita newline
  - `IsEntityFrameworkMigrationSource`, `IsTestSource`, `IsTestProject`
- `RerankerService`: boost para match de símbolo; penalidade config em query estrutural
- `ContextCompressionService`: colapso de usings, headers de licença, corpos contraídos
- `ContextualChunkingService`: prefixo correto por linguagem
- `RuleExtractionService`: heurísticas (BusinessException, ValidationMessage, ErrorContext, relevant lines); dedup
- `KnowledgeExtractionService`: heurísticas (HttpClient, MassTransit, TODO, FIXME); dedup
- `TextNormalizationService`: NormalizeSentence, LooksLikeBusinessRule, evidence extraction
- `HashService`: deterministicidade

#### Cobertura integração (alvo)
- `PgVectorService` com Testcontainers/Postgres + pgvector:
  - `UpsertChunkAsync` -> `SearchAsync` retorna o chunk
  - `SearchCodeAsync` com RRF (vector + text)
  - `SearchBusinessRulesAsync`, `SearchKnowledgeAsync`
  - `DeleteEntityFrameworkMigrationChunksAsync`, `DeleteTestChunksAsync`
  - `UpsertBusinessRuleCandidateAsync` (insert + update + skip)
  - `MarkExtractionChunkProcessedAsync` / `FailedAsync`
  - `GetSymbolCallersAsync`, `GetSymbolCalleesAsync`, `GetClassHierarchyAsync`
- Migrations SQL: aplicar `000` + `010` + `020` em banco novo; aplicar novamente em banco populado (idempotência)

#### Pacotes NuGet necessários
- `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`
- `FluentAssertions`
- `Testcontainers.PostgreSql`

### PB-007 — Split

#### Estrutura proposta
- `Services/Repositories/ChunkRepository.cs` — upsert/delete/search de chunks
- `Services/Repositories/RuleRepository.cs` — upsert/search/stats de rules + predicates de candidate
- `Services/Repositories/KnowledgeRepository.cs` — upsert/search/stats de knowledge + predicates
- `Services/Repositories/SymbolGraphRepository.cs` — symbols + relations + callers/callees/hierarchy
- `Services/SearchService.cs` — lógica de busca híbrida (RRF) compartilhada
- `Services/SqlPredicates.cs` — strings estáticas (RuleCandidatePredicate, KnowledgeCandidatePredicate, EF, Test, etc.)

#### Ordem
1. Extrair `SqlPredicates.cs` primeiro (só move strings — baixo risco)
2. Extrair `ChunkRepository` (testes de integração já cobrem)
3. Extrair `RuleRepository` e `KnowledgeRepository`
4. Extrair `SymbolGraphRepository`
5. Refatorar `PgVectorService` para facade ou remover se não usado

#### Validação
- Rodar `dotnet test` após cada extração
- Rodar `ai-memory search` em repo de referência antes/depois — comparar top 10
- Rodar `ai-memory mcp` e chamar `search_code` no Cursor — confirmar mesmo resultado

## Dependências

- PB-006 deve ser parcialmente done antes de PB-007 (testes precisam existir para refactor seguro)
- Ordem: 60% de PB-006 -> começar PB-007 -> terminar PB-006 com testes pós-split

## Riscos

- RS-003 (split introduz bug) — mitigar com testes antes
- Sprint pode spillar para Sprint 3 (XL + L em 1 semana é apertado)

## Plano de burndown ideal

```
Dia 1: criar projeto test, setup Testcontainers, primeiros testes ChunkingService
Dia 2: testes Reranker, ContextCompression, ContextualChunking, Hash
Dia 3: testes RuleExtraction, KnowledgeExtraction, TextNormalization
Dia 4: testes integração PgVectorService + migrations
Dia 5: PB-007 split (incremental, testes verde a cada passo)
```

## Sprint Review (a preencher)

- Items Done:
- Items não-Done:
- Cobertura alcançada:
- Demo realizada:

## Sprint Retrospective (a preencher)

Ver [`../07-retrospectives/sprint-2-retro.md`](../07-retrospectives/sprint-2-retro.md).
