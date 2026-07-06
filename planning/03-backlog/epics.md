# Épics

Épics agrupam items do backlog por tema de valor.

## EP-01 — Estabilização

**Goal**: destravar o projeto para desenvolver com segurança.

**Items**: PB-001, PB-002, PB-003, PB-004, PB-005

**Valor esperado**: build verde em qualquer máquina; setup reproduzível; busca sem lixo; base sólida para as próximas fases.

**Métrica de sucesso**: `dotnet build` verde do zero em Linux/macOS/Windows; setup completado sem warning.

**Sprint alvo**: Sprint 1

---

## EP-02 — Confiança

**Goal**: poder refatorar sem medo.

**Items**: PB-006, PB-007

**Valor esperado**: cobertura de testes >70% em serviços core; `PgVectorService` split em repositórios testáveis; bug em RRF/compressão detectado por teste.

**Métrica de sucesso**: `dotnet test` verde; cobertura mensurável; refactor de `PgVectorService` sem regressão.

**Sprint alvo**: Sprint 2

---

## EP-03 — Performance

**Goal**: indexar e buscar mais rápido sem mudar arquitetura.

**Items**: PB-008, PB-009, PB-010

**Valor esperado**: indexação de 100k chunks em <30 min (vs horas hoje); extração semântica resiliente a falhas transitórias.

**Métrica de sucesso**: benchmark antes/depois de `index chunks` em repo de referência.

**Sprint alvo**: Sprint 3

---

## EP-04 — MCP 2025-11-25 + Tray split

**Goal**: alinhar MCP ao spec mais recente estável e desacoplar tray da CLI.

**Items**: PB-011, PB-012, PB-013, PB-014, PB-015, PB-016, PB-017

**Valor esperado**: clientes pulam autorização em tools só-leitura (Tool Annotations); resultados renderizados estruturados (Structured Output); LLM se auto-corrigi em erro de input (Tool Execution Errors); binário MCP ~70% menor (tray split).

**Métrica de sucesso**: teste manual em Rider/Cursor confirma ausência de prompt de autorização em `search_code`; tamanho do nupkg medido antes/depois.

**Sprint alvo**: Sprint 4

---

## EP-05 — Maturidade

**Goal**: distribuir com segurança para outros times.

**Items**: PB-018, PB-019, PB-020, PB-021

**Valor esperado**: releases automatizados; memória sempre fresca (watcher real); heurísticas customizáveis por time; cache MCP eficiente.

**Métrica de sucesso**: pipeline CI verde em cada PR; release publicada via `git tag`; watcher mantém banco sincronizado após edição de arquivo.

**Sprint alvo**: Sprint 5

---

## EP-06 — MCP 2026-07-28 (futuro)

**Goal**: adotar próxima revisão do MCP quando estiver estável e suportada por clients.

**Items**: PB-022, PB-023, PB-024, PB-025, PB-026, PB-027

**Valor esperado**: economia de tokens no prompt via `CacheableResult`; coexistir com clients antigos via `server/discover`; recall melhor com MRTR; integração watch->IDE via `subscriptions/listen`.

**Métrica de sucesso**: spec `2026-07-28` estável publicada; >=2 clients-alvo suportam; teste de compatibilidade com cliente antigo passa.

**Sprint alvo**: Sprints futuros (após Jul/2026)

---

## Visão de épics por release

| Release | Épicos | Sprint |
|---|---|---|
| v0.2.0 | EP-01 + EP-02 | 1 + 2 |
| v0.3.0 | EP-03 | 3 |
| v0.4.0 | EP-04 | 4 |
| v0.5.0 | EP-05 | 5 |
| v0.6.0+ | EP-06 | Futuro |
