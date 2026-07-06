# Working Agreement

Acordos do time (mesmo sendo um time de um contribuidor + subagentes).

## Cadência

- **Sprint**: 1 semana (segunda a sexta)
- **Sprint Planning**: segunda, 30 min
- **Daily**: assíncrona, comentário no GitHub a cada dia trabalhado
- **Sprint Review**: sexta, 20 min
- **Retrospectiva**: sexta, 20 min, após Review

## Repositório e branches

- `main` sempre verde (build + testes passando)
- Branches por feature: `feat/PB-XXX-descricao`, `fix/PB-XXX-descricao`, `chore/PB-XXX-descricao`
- Branches `subagent-*` são temporárias; limpar após merge
- Conventional commits: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `chore:`, `ci:`
- Um PR por user story quando possível

## PRs e code review

- PR abre quando DoR está satisfeito
- Título referencia o ID: `[PB-001] Corrige build quebrado`
- Descrição inclui: o quê, por quê, como testar
- Auto-review antes de solicitar review
- Review por subagente opencode (prompt explícito para apontar problemas)
- Aprovação requer: build verde + testes passando + 0 warnings novos
- Squash and merge em `main`

## Definition of Ready (resumo)

Ver [`definition-of-ready.md`](definition-of-ready.md).

## Definition of Done (resumo)

Ver [`definition-of-done.md`](definition-of-done.md).

## Comunicação

- Decisões técnicas em ADRs, não em chat
- Mudanças de prioridade refletem-se no backlog dentro de 24h
- Bloqueios reportados no daily ou PR correspondente
- Documentação atualizada no mesmo PR que a mudança

## Princípios técnicos

- Não adicionar código sem teste (exceto spikes declarados)
- Sem comentários em código a menos que explicitem "por quê" não-óbvio
- Sem warnings novos
- Sem dependências novas sem ADR
- Sem migrações SQL sem teste de idempotência

## Princípios de produto

- Toda feature tem persona e dor mapeada
- Toda otimização tem métrica antes e depois
- Toda remoção tem rationale em ADR

## Subagentes opencode

- Subagentes `explore` para pesquisa de codebase antes de implementar
- Subagentes `general` para implementar PRs pequenos e focados
- Nenhum subagente commita direto em `main` — sempre via branch + PR
- Output de subagente é revisado pelo humano antes de merge

## Não escopos (anti-objetos)

- Não otimizar latência nesta fase (ONNX, HNSW, cache persistente) — ver ADR-003
- Não adicionar linguagens fora de .NET
- Não adicionar autenticação/OAuth ao MCP (uso local)
- Não adicionar UI web além do dashboard existente
