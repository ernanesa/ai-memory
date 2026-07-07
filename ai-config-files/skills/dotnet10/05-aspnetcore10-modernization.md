---
name: aspnetcore10-modernization
description: 'Modernizacao ASP.NET Core 10 para APIs e aplicacoes web. Use para revisar middleware, Minimal APIs, controllers, OpenAPI, auth, health checks e ProblemDetails.'
argument-hint: 'Program.cs, endpoint, controller ou API'
user-invocable: true
---

# Modernizacao ASP.NET Core 10

## Objetivo

Migrar APIs e apps web para .NET 10 preservando contrato, seguranca e operabilidade.

## Fluxo obrigatorio

1. Revisar pipeline de middleware e ordem de autenticacao, autorizacao, CORS e endpoints.
2. Validar contratos HTTP, status codes, DTOs, ProblemDetails e OpenAPI.
3. Checar health checks, logs, configuracao, rate limiting e headers.
4. Garantir testes de integracao para endpoints criticos.
5. Separar mudanca de framework de mudanca funcional.

## Nao fazer

- Nao alterar contrato publico sem versionamento ou comunicacao.
- Nao mover middleware sem explicar impacto.
- Nao transformar erro de dominio em 500 generico.

## Saida esperada

- Pontos de modernizacao.
- Riscos por endpoint.
- Testes necessarios.
- Plano incremental.
