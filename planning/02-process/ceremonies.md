# Cerimônias Scrum

## Sprint

- **Duração**: 1 semana
- **Início**: segunda-feira
- **Fim**: sexta-feira
- **Timebox**: 5 dias úteis

## Sprint Planning

- **Quando**: segunda, início do dia
- **Duração**: 30 minutos (timebox rígido)
- **Participantes**: PO, SM, Tech Lead, Dev Team
- **Entrada**: Product Backlog atualizado, items Ready, capacity confirmada
- **Saída**:
  - Sprint goal (1–2 frases)
  - Sprint backlog (items committed)
  - Estimativas refinadas se necessário
- **Formato**:
  1. PO apresenta sprint goal proposto (5 min)
  2. Time revisa items Ready no topo do backlog (15 min)
  3. Time commita items para o sprint (5 min)
  4. SM confirma blockers e dependências (5 min)

## Daily Scrum

- **Quando**: assíncrona, todo dia trabalhado
- **Duração**: 5 minutos
- **Formato**: comentário no GitHub Discussion ou issue do sprint
  - O que fiz ontem
  - O que vou fazer hoje
  - Blockers
- **Regra**: blocker identificado -> SM atuando em até 2h

## Sprint Review

- **Quando**: sexta, final do dia
- **Duração**: 20 minutos
- **Participantes**: PO, SM, Dev Team, stakeholders (se aplicável)
- **Entrada**: incremento do sprint, métricas de velocity
- **Saída**:
  - Demo do incremento
  - PO aceita/rejeita items
  - Atualização do backlog se necessário
  - Feedback de stakeholders registrado

## Sprint Retrospective

- **Quando**: sexta, após Review
- **Duração**: 20 minutos
- **Participantes**: SM, Dev Team (PO opcional)
- **Entrada**: sprint passado, métricas, feedback
- **Saída**:
  - O que funcionou
  - O que não funcionou
  - Ações concretas para próximo sprint (com dono e prazo)
- **Formato**: ver [`../07-retrospectives/retrospective-template.md`](../07-retrospectives/retrospective-template.md)
- **Arquivo**: `planning/07-retrospectives/sprint-X-retro.md`

## Backlog Refinement

- **Quando**: quarta, meio do sprint
- **Duração**: 30 minutos
- **Participantes**: PO, Tech Lead
- **Objetivo**: garantir que items do topo do backlog estão Ready para próximo Planning

## Release Planning

- **Quando**: ao final de cada release (não cada sprint)
- **Duração**: 1 hora
- **Saída**: release notes, tag git, NuGet publicado, comunicação

## Cadência visual

```
Seg  Ter  Qua  Qui  Sex
 |    |    |    |    |
Planning     Refinement   Review
 |    |    |    |    |
 Daily Daily Daily Daily Daily
                        |
                     Retrospective
                        |
                  (Release se aplicável)
```
