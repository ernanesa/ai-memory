---
name: dotnet10-breaking-changes-review
description: 'Revisao de breaking changes e mudancas de comportamento na migracao para .NET 10. Use antes de aceitar a migracao.'
argument-hint: 'area do sistema, erro, pacote ou endpoint'
user-invocable: true
---

# Revisao de Breaking Changes .NET 10

## Objetivo

Encontrar riscos reais de compatibilidade antes que eles virem incendio em producao.

## Fluxo obrigatorio

1. Procurar warnings, APIs obsoletas e mudancas de comportamento relevantes.
2. Revisar runtime, SDK, ASP.NET Core, EF Core, serializacao, autenticacao e hosting.
3. Comparar contratos publicos: rotas, DTOs, eventos, mensagens e status codes.
4. Separar risco confirmado, risco provavel e hipotese.
5. Recomendar testes objetivos para cada risco.

## Nao fazer

- Nao tratar todo warning como blocker.
- Nao esconder mudanca funcional dentro de PR de migracao.
- Nao sugerir workaround fragil sem teste de regressao.

## Saida esperada

- Breaking changes provaveis.
- Evidencias.
- Impacto.
- Mitigacao.
- Testes de regressao.
