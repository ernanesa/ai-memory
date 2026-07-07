# Registro de Riscos

Riscos identificados, com probabilidade, impacto e plano de mitigação.

## Convenções

- **ID**: `RS-XXX`
- **Probabilidade**: Alta, Média, Baixa
- **Impacto**: Alto, Médio, Baixo
- **Severidade**: P x I (critério para priorizar mitigação)
- **Status**: Aberto, Mitigado, Realizado, Evitado

## Riscos técnicos

### RS-001 — Clients MCP não suportam `2025-11-25`
- **Probabilidade**: Baixa (Cursor, Claude Desktop, Rider já suportam)
- **Impacto**: Alto (perde benefícios do upgrade)
- **Severidade**: Média
- **Mitigação**: manter version negotiation atual (`McpCommand.cs:138-145`) — clients antigos pedem `2024-11-05` e servidor responde ack. Testar em pelo menos 2 clients antes de release.
- **Status**: Aberto (até Sprint 4)

### RS-002 — Migration `010_compat.sql` quebra bancos existentes
- **Probabilidade**: Média
- **Impacto**: Alto (setup falha em máquinas já configuradas)
- **Severidade**: Alta
- **Mitigação**: usar `ADD COLUMN IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, `CREATE TABLE IF NOT EXISTS`. Testar em banco populado (Testcontainers). Adicionar teste de idempotência.
- **Status**: Aberto (até PB-003)

### RS-003 — Split de `PgVectorService` introduz bug
- **Probabilidade**: Média
- **Impacto**: Alto (busca para de funcionar)
- **Severidade**: Alta
- **Mitigação**: fazer split DEPOIS de PB-006 (testes). Garantir cobertura antes do refactor. Comparar resultados de busca antes/depois em repo de referência.
- **Status**: Aberto (até PB-007)

### RS-004 — Paralelização de extração causa race condition
- **Probabilidade**: Média
- **Impacto**: Médio (chunks marcados errados)
- **Severidade**: Média
- **Mitigação**: usar `Parallel.ForEachAsync` com `MaxDegreeOfParallelism`. `ai_extraction_chunk_state` updates em transação. Teste de concorrência.
- **Status**: Aberto (até PB-009)

### RS-005 — Batch embedding perde chunks em erro
- **Probabilidade**: Média
- **Impacto**: Médio (chunks faltam no banco)
- **Severidade**: Média
- **Mitigação**: em erro de batch, retentar chunks individualmente. Log de chunks perdidos.
- **Status**: Aberto (até PB-008)

### RS-006 — Tray split quebra autostart de usuários existentes
- **Probabilidade**: Média
- **Impacto**: Alto (tray para de iniciar no boot)
- **Severidade**: Alta
- **Mitigação**: `ai-memory tray update` detecta autostart antigo e re-cria apontando para `ai-memory-tray`. Documentar migration path no release notes.
- **Status**: Aberto (até PB-017)

### RS-007 — Watcher real dispara indexação em loop
- **Probabilidade**: Média
- **Impacto**: Médio (CPU alta, banco sobrecarregado)
- **Severidade**: Média
- **Mitigação**: debounce 500ms. Ignorar `bin/`, `obj/`, `.git/`. Throttling por arquivo.
- **Status**: Aberto (até PB-019)

## Riscos de produto

### RS-008 — Single-contributor é gargalo
- **Probabilidade**: Alta (certo)
- **Impacto**: Médio (velocidade limitada)
- **Severidade**: Média
- **Mitigação**: subagentes opencode paralelizam pesquisa e PRs pequenos. ADRs reduzem revisão de decisão. Sprints curtos (1 semana) limitam WIP.
- **Status**: Aceito (crônico)

### RS-009 — Conflito de interesse PO/Dev/Tech Lead
- **Probabilidade**: Alta
- **Impacto**: Baixo-Médio (decisões enviesadas)
- **Severidade**: Média
- **Mitigação**: ADRs forçam explicitar trade-offs. Subagentes fazem code review "de fora". DoD tem critérios objetivos.
- **Status**: Aceito (mitigado)

### RS-010 — Usuários não adotam por setup complexo
- **Probabilidade**: Média
- **Impacto**: Alto (projeto não decola)
- **Severidade**: Alta
- **Mitigação**: setup já é interativo. Considerar reduzir dependências em Fase 6 (ONNX embutido remove Ollama para embedding).
- **Status**: Aberto (revisitado em Fase 6)

### RS-011 — Spec MCP `2026-07-28` muda entre RC e final
- **Probabilidade**: Alta (é RC)
- **Impacto**: Baixo (não adotamos nesta fase)
- **Severidade**: Baixa
- **Mitigação**: ADR-002 — só adotar quando virar stable.
- **Status**: Mitigado

## Riscos de operação

### RS-012 — Release v0.4.0 quebra bancos de usuários v0.1.5
- **Probabilidade**: Baixa (com mitigation)
- **Impacto**: Alto (perda de dados ou setup)
- **Severidade**: Média
- **Mitigação**: migrations aditivas (IF NOT EXISTS). Release notes com migration path. Testar upgrade de v0.1.5 -> v0.4.0 em Testcontainers.
- **Status**: Aberto (até Sprint 4)

### RS-013 — NuGet package quebra em target framework
- **Probabilidade**: Baixa
- **Impacto**: Médio (instalação falha)
- **Severidade**: Baixa
- **Mitigação**: CI em Linux/macOS/Windows antes de release.
- **Status**: Aberto (até PB-018)

## Riscos baixos

### RS-014 — Ollama muda API `/api/embeddings`
- **Probabilidade**: Baixa
- **Impacto**: Médio
- **Severidade**: Baixa
- **Mitigação**: monitorar changelog Ollama. Detectar versão no `doctor`.

### RS-015 — pgvector muda formato de vetor
- **Probabilidade**: Baixa
- **Impacto**: Alto
- **Severidade**: Baixa
- **Mitigação**: pinar versão pgvector no setup. Testar antes de upgrade.

## Top 3 riscos para monitorar

1. **RS-002** (migration quebra bancos existentes) — crítico para adoption
2. **RS-003** (split introduz bug) — crítico para confiança
3. **RS-006** (tray split quebra autostart) — crítico para UX
