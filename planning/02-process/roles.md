# Papéis Scrum

Este projeto é mantido por uma pessoa principal (Ernane) assistida por subagentes opencode. Os papéis abaixo são assumidos por essa pessoa em momentos diferentes, com responsabilidades explícitas para evitar conflito de interesse.

## RACI por atividade

| Atividade | PO | SM | Tech Lead | Dev | QA | DevOps |
|---|---|---|---|---|---|---|
| Visão e priorização | R/A | C | C | I | I | I |
| Product Backlog | R/A | C | C | I | I | I |
| Sprint Planning | C | R/A | C | R | I | I |
| Daily | I | F | C | R/A | I | I |
| Implementação | I | I | C | R/A | C | C |
| Code review | I | I | R/A | R | C | I |
| Testes | I | I | C | R | R/A | I |
| DoR / DoD | C | R | R/A | C | C | C |
| Release | C | I | C | R | C | R/A |
| Retrospectiva | C | R/A | R | R | R | R |
| ADRs | I | I | R/A | C | I | C |
| CI/CD | I | I | C | R | C | R/A |

Legenda: **R** = responsável, **A** = accountable, **C** = consultado, **I** = informado, **F** = facilitador.

## Papel — Product Owner

- **Atribuição**: Ernane (autor do projeto)
- **Mandato**: maximizar valor entregue pelo time
- **Responsabilidades**:
  - Manter e priorizar o Product Backlog
  - Definir e comunicar a visão do produto
  - Aceitar ou rejeitar incrementos ao final de cada sprint
  - Decidir release dates e conteúdo
  - Clarificar user stories para o time
- **Não faz**: estimar tecnicamente, decidir arquitetura

## Papel — Scrum Master

- **Atribuição**: Ernane (rotativa, em sprint planning e retrospectiva)
- **Mandato**: garantir que o Scrum seja entendido e aplicado
- **Responsabilidades**:
  - Facilitar cerimônias (Planning, Review, Retrospective)
  - Remover impedimentos
  - Garantir que artefatos estejam atualizados
  - Proteger o time de interrupções
  - Coletar métricas de velocity e burndown
- **Não faz**: priorizar backlog, escrever código

## Papel — Tech Lead / Arquiteto

- **Atribuição**: Ernane
- **Mandato**: sustentabilidade técnica do produto
- **Responsabilidades**:
  - Escrever e manter ADRs
  - Aprovar refactors estruturais
  - Code review final em PRs de risco
  - Decidir stack e bibliotecas (com rationale documentado)
  - Identificar e priorizar dívida técnica
- **Não faz**: priorizar backlog de produto

## Papel — Dev Team

- **Atribuição**: Ernane + subagentes opencode (general, explore)
- **Mandato**: entregar incrementos潜在mente shippable
- **Responsabilidades**:
  - Estimar items do backlog
  - Commitar com sprint goal
  - Implementar, testar, documentar
  - Reportar bloqueios no daily
- **Princípios**:
  - Um PR por user story quando possível
  - PRs pequenos e focados
  - TODO sem dono é débito

## Papel — QA

- **Atribuição**: Ernane + suíte de testes automatizados
- **Mandato**: confiança para refatorar
- **Responsabilidades**:
  - Definir cobertura mínima por serviço
  - Escrever testes unitários e de integração
  - Validar fixes de bug com teste regressivo
  - Reportar bugs como user stories

## Papel — DevOps

- **Atribuição**: Ernane
- **Mandato**: releases reproduzíveis
- **Responsabilidades**:
  - Configurar e manter CI/CD
  - Empacotar NuGet
  - Versionamento semântico
  - Monitorar pipeline e fixar builds vermelhos

## Stakeholders

- **Comunidade .NET open-source**: feedback via GitHub issues
- **Agentes IA consumidores** (indiretos): Claude, Cursor, Codex, Rider — revelam dores via comportamento (ex.: pedir confirmação indica falta de `readOnlyHint`)
- **Outros times** que venham a adotar a tool

## Conflito de interesse (single-contributor)

Como uma pessoa assume múltiplos papéis, há risco de:
- PO priorizar dívida técnica como se fosse feature
- Dev subestimar esforço para agradar PO
- Tech Lead aprovar próprio PR sem review externo

**Mitigação**:
- ADRs forçam explicitar trade-offs antes de implementar
- Subagentes opencode fazem code review "de fora" (prompt explícito para apontar problemas)
- Definition of Done tem critérios objetivos (testes passam, build verde)
- Retrospectiva documenta conflitos quando ocorrem
