# Release Notes — v0.2.0

Data: AAAA-MM-DD (após Sprint 2)
Sprint: 1 + 2
Épico(s): EP-01 (Estabilização), EP-02 (Confiança)

## Resumo

Release de estabilização. Build verde em qualquer máquina, busca sem chunks órfãos, migrations reproduzíveis, testes automatizados cobrindo serviços core, e `PgVectorService` split em repositórios testáveis.

## Breaking changes

Nenhuma. Compatível com v0.1.5.

## Migration path

```bash
dotnet tool update -g AiMemory.Tool
ai-memory setup  # re-aplica migrations idempotentes
```

## Novidades

### Estabilização
- [PB-001] Build corrigido em ambientes onde `Directory.SetUnixFileMode` não resolvia
- [PB-002] Chunks órfãos (arquivos deletados) são removidos do banco após indexação
- [PB-003] Migrations SQL renomeadas para ordem numérica (`000`, `010`, `020`) com rastreamento por hash
- [PB-004] `MaxChunkLength` unificado em 6000 (código e README alinhados)
- [PB-005] Stdout do MCP redirecionado para `TextWriter.Null` — stream JSON-RPC nunca corrompido

### Confiança
- [PB-006] Testes unitários (xUnit + FluentAssertions) e integração (Testcontainers/Postgres)
- [PB-007] `PgVectorService` (1290 linhas) split em `ChunkRepository`, `RuleRepository`, `KnowledgeRepository`, `SymbolGraphRepository`, `SearchService`, `SqlPredicates`

## Bug fixes

- [PB-001] Build quebrado em Linux com .NET 10 SDK
- [PB-002] Busca retornava código de arquivos deletados

## Dívida técnica paga

- TD-001 (zero testes) — pago por PB-006
- TD-002 (PgVectorService god class) — pago por PB-007
- TD-003 (chunks órfãos) — pago por PB-002
- TD-004 (migrations instáveis) — pago por PB-003
- TD-005 (build quebrado) — pago por PB-001
- TD-011 (stdout não guardado) — pago por PB-005
- TD-013 (MaxChunkLength inconsistente) — pago por PB-004

## Métricas

| Métrica | Antes (v0.1.5) | Depois (v0.2.0) |
|---|---|---|
| Build do zero em Linux | Falha | Verde |
| Cobertura de testes (serviços core) | 0% | >70% |
| Linhas em `PgVectorService` | 1290 | <400 por classe |
| Migrations SQL determinísticas | Não | Sim (com hash) |

## Testado em

- [ ] Linux (Ubuntu)
- [ ] macOS
- [ ] Windows 11
- [ ] dotnet build -c Release
- [ ] dotnet test

## ADRs relacionados

- [ADR-006] Testcontainers para testes de integração
- [ADR-008] Migrations SQL com tabela `ai_schema_migrations` e ordem numérica
