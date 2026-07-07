# Release Notes — v0.5.0

Data: AAAA-MM-DD (após Sprint 5)
Sprint: 5
Épico(s): EP-05 (Maturidade)

## Resumo

Release de maturidade operacional. CI/CD automatizado, watcher real com `FileSystemWatcher`, heurísticas configuráveis por time, e cache MCP LRU com TTL. Primeira release publicada via pipeline (não manual).

## Breaking changes

Nenhuma. Heurísticas defaults continuam pt-BR (compatibilidade).

## Migration path

```bash
dotnet tool update -g AiMemory.Tool
# Se usa tray:
dotnet tool update -g AiMemory.Tray
ai-memory tray update
```

## Novidades

### Operação
- [PB-018] CI/CD com GitHub Actions: build + testes em cada PR (Linux + macOS + Windows); publicação NuGet em tag `vX.Y.Z`
- [PB-019] `ai-memory watch` agora usa `FileSystemWatcher` real com debounce 500ms — memória fica sempre fresca sem intervenção

### Configuração
- [PB-020] Heurísticas de regras de negócio e conhecimento configuráveis via `~/.aimemory/patterns.json` — times em inglês ou outros domínios podem customizar sem editar SQL

### Performance de busca
- [PB-021] Cache de embedding MCP trocado de FIFO (128 entries) para LRU com TTL (1000 entries, 1h) — hit-rate maior em sessões longas

## Bug fixes

- `ai-memory watch` não re-roda mais `index` inteiro a cada chamada

## Dívida técnica paga

- TD-012 (watch não é real) — pago por PB-019
- TD-014 (heurísticas hard-coded) — pago por PB-020
- TD-015 (cache FIFO) — pago por PB-021
- TD-016 (sem CI/CD) — pago por PB-018
- TD-017 (sem release tags) — pago por PB-018

## Métricas

| Métrica | Antes (v0.4.0) | Depois (v0.5.0) |
|---|---|---|
| Pipeline CI em PR | Não | Sim (3 OSes) |
| Releases automatizadas | Não | Sim (`git tag`) |
| Tempo para reindexar 1 arquivo editado | Re-roda `index` inteiro | <2s (watcher + debounce) |
| Cache MCP hit-rate (sessão longa) | Baixo (FIFO 128) | Alto (LRU 1000 + TTL 1h) |
| Heurísticas customizáveis | Não (hard-coded) | Sim (patterns.json) |

## Testado em

- [ ] Pipeline CI verde em Linux, macOS, Windows
- [ ] Release publicada via `git tag v0.5.0`
- [ ] `ai-memory watch` reage a edição em <2s
- [ ] `patterns.json` customizado funciona

## ADRs relacionados

- (nenhum novo)
