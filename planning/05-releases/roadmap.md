# Roadmap de Releases

Versionamento semântico (SemVer): `MAJOR.MINOR.PATCH`.

- **MAJOR**: breaking changes (schema não compatível, comportamento de MCP quebra clients)
- **MINOR**: features novas compatíveis
- **PATCH**: bug fixes

## Releases planejadas

### v0.2.0 — Estabilização

- **Sprint alvo**: 1 + 2
- **Conteúdo**:
  - PB-001 Build corrigido
  - PB-002 Chunks órfãos limpos
  - PB-003 Migrations estáveis
  - PB-004 `MaxChunkLength` alinhado
  - PB-005 Stdout guard no MCP
  - PB-006 Testes (unitários + integração)
  - PB-007 Split `PgVectorService`
- **Breaking changes**: nenhum (compatível com v0.1.5)
- **Migration path**: `dotnet tool update -g AiMemory.Tool && ai-memory setup` (re-aplica migrations idempotentes)
- **Critério de release**: `dotnet test` verde, `dotnet build` verde em Linux/macOS/Windows, cobertura >70% em serviços core

### v0.3.0 — Performance

- **Sprint alvo**: 3
- **Conteúdo**:
  - PB-008 Batch embedding Ollama
  - PB-009 Paralelizar extração rules/knowledge
  - PB-010 Retry/backoff Polly
- **Breaking changes**: nenhum
- **Ganho esperado**: indexação 10× mais rápida (benchmark em repo de referência)
- **Critério de release**: benchmark documenta speedup, `dotnet test` verde

### v0.4.0 — MCP 2025-11-25 + Tray split

- **Sprint alvo**: 4
- **Conteúdo**:
  - PB-011 Upgrade MCP `2025-11-25`
  - PB-012 Tool Annotations
  - PB-013 Structured Output
  - PB-014 Ícones em tools
  - PB-015 Tool Execution Errors
  - PB-016 `title` field
  - PB-017 Tray split em `AiMemory.Tray.csproj`
- **Breaking changes**:
  - **MCP**: protocol version `2024-11-05` -> `2025-11-25` (clients antigos ainda conectam via version negotiation)
  - **Distribuição**: tray agora é package separado `AiMemory.Tray`. Usuários v0.1.5 precisam rodar `ai-memory tray update` para re-criar autostart apontando para `ai-memory-tray`
- **Migration path**:
  1. `dotnet tool update -g AiMemory.Tool`
  2. `dotnet tool install -g AiMemory.Tray`
  3. `ai-memory tray update`
  4. Release notes com instruções detalhadas
- **Critério de release**: testado em Rider, Cursor, Claude Desktop; tamanho nupkg `AiMemory.Tool` -70%

### v0.5.0 — Maturidade

- **Sprint alvo**: 5
- **Conteúdo**:
  - PB-018 CI/CD
  - PB-019 Watcher real
  - PB-020 Heurísticas configuráveis
  - PB-021 Cache LRU + TTL
- **Breaking changes**: nenhum (heurísticas defaults continuam pt-BR)
- **Critério de release**: pipeline CI verde em 3 OSes, release publicada via `git tag`

### v0.6.0+ — MCP 2026-07-28 (futuro)

- **Sprint alvo**: futuro (após spec virar stable + clients suportarem)
- **Conteúdo**:
  - PB-022 Upgrade MCP `2026-07-28`
  - PB-023 `CacheableResult`
  - PB-024 `server/discover`
  - PB-025 Stateless
  - PB-026 MRTR
  - PB-027 `subscriptions/listen`
- **Breaking changes**: provavelmente MAJOR bump (stateless remove `initialize`)
- **Pré-requisitos**:
  - Spec `2026-07-28` publicada como stable (não RC)
  - Pelo menos 2 clients-alvo confirmam suporte
  - ADR-002 revisado

## Timeline visual

```
Sprint 1 ─┐
          ├─ v0.2.0 (Estabilização)
Sprint 2 ─┘

Sprint 3 ─── v0.3.0 (Performance)

Sprint 4 ─── v0.4.0 (MCP 2025-11-25 + Tray split)

Sprint 5 ─── v0.5.0 (Maturidade)

Futuro ───── v0.6.0+ (MCP 2026-07-28)
```

## Versão atual

- **`0.1.5`** (em `ai-memory.csproj:12`) — última publicada manualmente
- Próxima release: **`0.2.0`**
