---
name: ma9-context-first-response
description: 'Forca resposta baseada em contexto antes de opinar sobre codigo, arquitetura ou regra de negocio. Use quando pedir analise tecnica, explicacao de codigo, proposta de implementacao, revisao arquitetural ou recomendacao de padrao. Executa fluxo: consultar memoria, buscar codigo relacionado, buscar regras de negocio, buscar decisoes arquiteturais, e so depois responder com fatos vs inferencias, incertezas e referencias de arquivos.'
argument-hint: 'Tema/pergunta tecnica que precisa de resposta com lastro em codigo e contexto'
user-invocable: true
---

# Context First Response

## Objetivo
Gerar respostas tecnicas com rastreabilidade, reduzindo alucinacao e sugestoes que conflitem com o codigo existente.

## Quando usar
- Perguntas sobre codigo, arquitetura, regra de negocio ou trade-offs tecnicos.
- Pedidos de implementacao, refatoracao, code review ou troubleshooting.
- Situacoes em que o usuario quer evidencias concretas no repositorio.

## Nao usar
- Conversa casual sem conteudo tecnico.
- Solicitacoes sem necessidade de validacao por codigo (ex.: texto institucional).

## Fluxo obrigatorio
1. Consultar ai-memory antes de qualquer conclusao.
2. Buscar codigo relacionado ao pedido (arquivos, simbolos, chamadas e testes).
3. Buscar regras de negocio relacionadas (docs, contratos, validacoes, regras em codigo).
4. Buscar decisoes arquiteturais relacionadas (ADRs, convencoes, camadas, dependencias).
5. So entao montar a resposta final.

## Regras de decisao
- Se faltar evidencias em um dos 4 blocos (memoria, codigo, negocio, arquitetura), declarar explicitamente a lacuna.
- Se houver conflito entre sugestao e implementacao atual, priorizar aderencia ao codigo existente e sinalizar alternativa como opcional.
- Se nao houver certeza suficiente, perguntar apenas o minimo necessario para desbloquear.

## Criterios de qualidade antes de responder
- Ha pelo menos uma evidencia concreta de codigo relacionada ao tema.
- Ha indicacao clara do que e fato observado e do que e inferencia.
- Ha secao de incertezas/riscos quando aplicavel.
- Nao ha recomendacao que contradiga padrao vigente sem justificativa explicita.

## Formato de resposta
- Arquivos relevantes: liste os arquivos usados como base.
- Fatos observados: afirmacoes verificadas no contexto.
- Inferencias: hipoteses ou extrapolacoes, rotuladas como tal.
- Incertezas: pontos que dependem de confirmacao.
- Recomendacao aderente: proposta que respeita o codigo existente.

## Checklist rapido
- ai-memory consultada
- codigo relacionado consultado
- regra de negocio consultada
- decisoes arquiteturais consultadas
- fatos vs inferencias separados
- incertezas destacadas
- sem conflito com padrao existente
