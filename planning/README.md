# Planejamento Scrum — ai-memory

Índice completo dos artefatos de planejamento.

## Estrutura

```
planning/
├── 01-product/         Visão, personas, problema, história
├── 02-process/         Papéis, working agreement, DoR, DoD, cerimônias
├── 03-backlog/         Product backlog, épics, user stories, dívida, riscos
├── 04-sprints/         Planos de sprint 1 a 5
├── 05-releases/        Roadmap e template de release notes
├── 06-technical/       ADRs e restrições técnicas
└── 07-retrospectives/  Templates e retrospectivas passadas
```

## Ordem de leitura sugerida

1. [`01-product/product-vision.md`](01-product/product-vision.md) — o porquê
2. [`01-product/problem-statement.md`](01-product/problem-statement.md) — o que dói hoje
3. [`03-backlog/product-backlog.md`](03-backlog/product-backlog.md) — o que vamos fazer
4. [`05-releases/roadmap.md`](05-releases/roadmap.md) — quando entregar
5. [`04-sprints/sprint-1.md`](04-sprints/sprint-1.md) — o próximo passo
6. [`06-technical/architecture-decisions.md`](06-technical/architecture-decisions.md) — decisões técnicas

## Convenções

- **IDs**: `PB-XXX` (product backlog item), `US-XXX` (user story), `EP-XX` (epic), `ADR-XXX` (architecture decision record), `TD-XXX` (technical debt), `RS-XXX` (risk), `SP-X` (sprint)
- **Prioridade**: P0 (bloqueante), P1 (alta), P2 (média), P3 (baixa)
- **Tamanho**: XS, S, M, L, XL (story points aproximados)
- **Status**: To Do, In Progress, Review, Done, Blocked, Cancelled
