# Release Notes — v0.3.0

Data: AAAA-MM-DD (após Sprint 3)
Sprint: 3
Épico(s): EP-03 (Performance)

## Resumo

Release de performance. Indexação 10× mais rápida via batch embedding e paralelismo. Extração semântica resiliente a falhas transitórias do Ollama.

## Breaking changes

Nenhuma. Compatível com v0.2.0.

## Migration path

```bash
dotnet tool update -g AiMemory.Tool
```

## Novidades

### Performance
- [PB-008] `OllamaService.EmbedBatchAsync` envia múltiplos chunks por request ao Ollama (`/api/embed` com `input: []`)
- [PB-009] Extração `rules`/`knowledge` usa `Parallel.ForEachAsync` com `--parallelism` configurável
- [PB-010] Retry/backoff com Polly v8 em `OllamaService` (3 retentativas, backoff exponencial + jitter)

## Bug fixes

- Falhas transitórias do Ollama não abortam mais pipeline de extração semântica

## Dívida técnica paga

- TD-006 (extração sequencial) — pago por PB-009
- TD-007 (embedding sem batch) — pago por PB-008
- TD-010 (sem retry no Ollama) — pago por PB-010

## Métricas

| Métrica | Antes (v0.2.0) | Depois (v0.3.0) |
|---|---|---|
| Tempo `index chunks` (100k chunks) | Xh | <30m (alvo) |
| Tempo `index rules --semantic` (5k candidatos) | Xh | Ym |
| Round-trips HTTP por indexação | N | N/10 |
| Cores utilizados em extração | 1 | N (configurável) |

> Benchmarks em repo de referência (`claps`) — preencher com valores reais após sprint.

## Testado em

- [ ] Linux
- [ ] macOS
- [ ] Windows 11
- [ ] dotnet build -c Release
- [ ] dotnet test
- [ ] benchmark antes/depois

## ADRs relacionados

- [ADR-007] Polly para retry/backoff
