---
name: aspnetcore-integration-tests
description: 'Testes de integracao para ASP.NET Core. Use para criar ou revisar testes de APIs com WebApplicationFactory, TestServer, banco isolado e autenticacao fake segura.'
argument-hint: 'endpoint, fluxo, controller ou contrato HTTP'
user-invocable: true
---

# Testes de Integracao ASP.NET Core

## Objetivo

Validar comportamento real de APIs sem depender de ambiente produtivo ou estado compartilhado.

## Fluxo obrigatorio

1. Testar status code, payload, headers, ProblemDetails e efeitos persistidos.
2. Isolar banco e dependencias externas com doubles ou containers.
3. Configurar autenticacao fake apenas no projeto de teste.
4. Cobrir happy path, validacao, autorizacao e idempotencia.
5. Garantir limpeza de estado entre testes.

## Nao fazer

- Nao usar banco de desenvolvimento ou producao.
- Nao mockar tanto que o teste deixa de integrar.
- Nao ignorar auth quando ela faz parte do contrato.

## Saida esperada

- Casos de teste.
- Setup recomendado.
- Dados de teste.
- Comandos de validacao.
