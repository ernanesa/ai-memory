---
name: efcore10-modernization
description: 'Modernizacao EF Core 10 com foco em seguranca, performance e previsibilidade. Use para revisar DbContext, queries, migrations, filtros globais, transacoes e tracking.'
argument-hint: 'DbContext, query, migration ou repositorio'
user-invocable: true
---

# Modernizacao EF Core 10

## Objetivo

Migrar persistencia com seguranca, evitando regressao em dados, performance e regras de negocio.

## Fluxo obrigatorio

1. Revisar DbContext, mappings, migrations, queries e transacoes.
2. Procurar N+1, tracking indevido, includes excessivos, filtros globais e consultas sem paginacao.
3. Separar mudanca de schema de mudanca de query.
4. Validar impacto em indices, concorrencia e rollback.
5. Sugerir testes de integracao para queries criticas.

## Nao fazer

- Nao gerar migration sem entender impacto produtivo.
- Nao usar Include como martelo universal.
- Nao trocar query por raw SQL sem motivo forte.

## Saida esperada

- Achados por query/tabela.
- Risco de migration.
- Plano de validacao.
- Sugestao incremental.
