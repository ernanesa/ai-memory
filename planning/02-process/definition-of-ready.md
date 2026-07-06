# Definition of Ready (DoR)

Um item do backlog está **Ready** quando atende a todos os critérios abaixo. Itens não-Ready não entram em Sprint Planning.

## Critérios gerais

- [ ] Tem ID único (`PB-XXX`)
- [ ] Tem título descritivo (verbo + objeto)
- [ ] Tem persona mapeada (P1–P5)
- [ ] Tem dor ou ganho justificando prioridade
- [ ] Tem critérios de aceitação testáveis (Given/When/Then quando aplicável)
- [ ] Tem estimativa de tamanho (XS/S/M/L/XL)
- [ ] Tem dependências identificadas (outros PB-XXX)
- [ ] Não tem bloqueios externos não resolvidos

## Critérios técnicos

- [ ] Locais afetados no código identificados (`file:line`)
- [ ] Se toca SQL: migration tem teste de idempotência
- [ ] Se toca MCP: versão de protocolo alvo identificada
- [ ] Se adiciona dependência: ADR rascunhado
- [ ] Se remove comportamento: impacto em usuários existentes documentado

## Critérios para user stories

- [ ] Formato "As a [persona], I want [feature], so that [benefit]"
- [ ] Critérios de aceitação cobrem caminho feliz + caminhos de erro
- [ ] Não ambígua (duas pessoas diferentes entendem o mesmo)
- [ ] Testável em <1 hora após implementação

## Anti-padrões (não está Ready)

- "Refatorar X" sem critério de sucesso
- "Melhorar performance" sem métrica alvo
- "Atualizar lib" sem ADR
- Item que mistura 3 concerns (split antes de Ready)
- Item sem dor associada (provavelmente é ouro no backlog)

## Checklist de entrada em Sprint Planning

Antes de o Scrum Master abrir Planning, confirma:

1. Items P0/P1 do backlog estão Ready?
2. Items do sprint anterior marcados como Done ou cancelados?
3. Capacity do time confirmada (férias, viagem, etc.)?
4. Sprint goal proposto pelo PO?
