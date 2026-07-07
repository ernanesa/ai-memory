---
name: dotnet10-api-contracts-openapi
description: 'Protecao de contratos HTTP e OpenAPI durante migracao .NET 10. Use para garantir que consumidores internos/externos nao quebrem.'
argument-hint: 'API, swagger/openapi, DTO ou endpoint'
user-invocable: true
---

# Contratos HTTP e OpenAPI .NET 10

## Objetivo

Preservar contratos publicos enquanto o runtime muda por baixo do capo.

## Fluxo obrigatorio

1. Comparar rotas, verbos, status codes, schemas, headers e auth antes/depois.
2. Identificar breaking changes em DTOs, nullability, enum, data/hora e paginacao.
3. Revisar ProblemDetails e erros de validacao.
4. Recomendar versionamento ou compat layer quando necessario.
5. Gerar checklist de testes de contrato.

## Nao fazer

- Nao renomear rota/DTO por estetica em PR de migracao.
- Nao mudar status code sem justificativa.
- Nao confiar apenas em teste unitario para contrato publico.

## Saida esperada

- Contratos impactados.
- Breaking changes.
- Plano de compatibilidade.
- Testes de contrato.
