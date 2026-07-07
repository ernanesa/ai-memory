# Sprint 1 — Destravar

- **Datas**: semana 1
- **Goal**: build verde do zero, busca sem lixo, setup reproduzível
- **Release alvo**: v0.2.0 (com Sprint 2)
- **Capacity**: 1 contribuidor + subagentes, ~20h/semana

## Sprint Backlog

| ID | Item | Tamanho | Prioridade | Status |
|---|---|---|---|---|
| PB-001 | Corrigir build quebrado (`SetUnixFileMode`) | XS | P0 | To Do |
| PB-002 | Limpar chunks órfãos após indexação | S | P0 | To Do |
| PB-003 | Estabilizar migrations SQL | S | P0 | To Do |
| PB-004 | Alinhar `MaxChunkLength` | XS | P1 | To Do |
| PB-005 | Guard stdout no MCP | XS | P1 | To Do |

**Total estimado**: ~7h (XS+XS+XS+S+S)

## Sprint Goal

"Build verde em qualquer máquina. Busca não retorna código fantasma. Setup reproduzível entre máquinas. Base sólida para Sprint 2."

## Critérios de aceitação do sprint

- [ ] `dotnet build -c Debug` verde do zero em Linux
- [ ] `dotnet build -c Release` verde
- [ ] DELETE de chunks órfãos implementado e testado manualmente
- [ ] Migrations renomeadas (`000`, `010`, `020`) e aplicadas em ordem
- [ ] `MaxChunkLength` unificado (código = README)
- [ ] Stdout do MCP só contém JSON-RPC
- [ ] Nenhum warning novo

## Tasks detalhadas

### PB-001 — Corrigir build
- Local: `Configuration/ConfigService.cs:300`
- Plano: envolver `Directory.SetUnixFileMode` com `#if !NET10_0_OR_GREATER` ou usar reflexão via `typeof(Directory).GetMethod("SetUnixFileMode", ...)`
- Alternativa: `[SupportedOSPlatform("linux")]` no método que chama
- Verificar outras chamadas Unix-only no mesmo arquivo (linhas 290–310)
- Teste: build verde em Linux

### PB-002 — Limpar órfãos
- Local: `Commands/IndexCommand.cs:102` (`IndexChunksAsync`) e `Services/PgVectorService.cs`
- Plano:
  1. Adicionar método `DeleteOrphanChunksAsync(workspace, projects, currentFilePaths)` em `PgVectorService`
  2. Chamar após `Parallel.ForEachAsync` em cada projeto
  3. Log: "removidos N chunks órfãos"
- Verificar FK `ai_business_rules.chunk_id` e `ai_knowledge.chunk_id` (já são ON DELETE CASCADE — regras somem juntos; revisar se é desejado ou se deve nullable)
- Teste manual: indexar projeto, deletar arquivo, re-indexar, confirmar DELETE

### PB-003 — Estabilizar migrations
- Renomear:
  - `sql/001_create_schema.sql` -> `sql/010_compat.sql`
  - `sql/001_hybrid_search.sql` -> `sql/020_hybrid_search.sql`
- Garantir que `000_schema.sql`, `010_compat.sql`, `020_hybrid_search.sql` aplicam em sequência numérica
- `SetupCommand.cs:436` já usa `OrderBy` — agora lexical funciona porque `000 < 010 < 020`
- Confirmar `EnsureSchemaMigrationsTableAsync` em `SetupCommand.cs:449` rastreia por nome
- Adicionar hash SHA256 do conteúdo na tabela de migrations
- Teste: rodar setup em banco novo e em banco v0.1.5 — ambos funcionam

### PB-004 — Alinhar `MaxChunkLength`
- `ChunkingService.cs:11`: trocar `MaxChunkLength = 1_000` para `6_000`
- Revisar `ChunkingService.cs:90` (`content.Length <= 8_000` — decide: ajustar para 6000 ou manter separado)
- README já diz ~6000 — confirmar
- Não adicionar teste agora (vai em PB-006)

### PB-005 — Guard stdout no MCP
- `Commands/McpCommand.cs:41` (`RunAsync`)
- Antes do loop, fazer: `Console.SetOut(TextWriter.Null);`
- Logs vão para `Console.Error` (já é padrão em `McpCommand.cs:43`)
- Verificar que nenhum serviço chamado por tool usa `Console.Out` para algo importante
- Teste: rodar `ai-memory mcp` e validar stdout só tem JSON-RPC

## Dependências e bloqueios

- Sem dependências externas
- Ordem sugerida: PB-001 primeiro (destrava build) -> PB-004, PB-005 (rápidos) -> PB-003 -> PB-002

## Riscos do sprint

- RS-002 (migration quebra bancos existentes) — mitigar com IF NOT EXISTS
- PB-002 pode revelar bugs em FK cascade — testar antes de commitar

## Definition of Done (aplicável a cada item)

Ver [`../02-process/definition-of-done.md`](../02-process/definition-of-done.md).

## Plano de burndown ideal

```
Dia 1: PB-001 done (XS)
Dia 2: PB-004 done (XS), PB-005 done (XS)
Dia 3: PB-003 done (S)
Dia 4: PB-002 done (S)
Dia 5: review, retrospective, polish
```

## Sprint Review (a preencher)

- Items Done:
- Items não-Done:
- Demo realizada:
- PO aceitou:

## Sprint Retrospective (a preencher)

Ver [`../07-retrospectives/sprint-1-retro.md`](../07-retrospectives/sprint-1-retro.md).
