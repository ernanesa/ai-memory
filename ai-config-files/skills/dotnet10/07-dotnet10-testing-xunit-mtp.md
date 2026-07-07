---
name: dotnet10-testing-xunit-mtp
description: 'Padronizacao de testes xUnit em .NET 10 e Microsoft.Testing.Platform. Use para organizar unit tests, fixtures, traits, nomenclatura e execucao via dotnet test.'
argument-hint: 'projeto de testes, suite, classe ou padrao desejado'
user-invocable: true
---

# Testes xUnit e MTP em .NET 10

## Objetivo

Padronizar testes para que a migracao para .NET 10 tenha rede de seguranca real.

## Fluxo obrigatorio

1. Separar unit, integration, contract e smoke tests.
2. Validar padrao AAA ou Given/When/Then.
3. Checar isolamento, determinismo, fixtures e traits.
4. Avaliar VSTest vs Microsoft.Testing.Platform antes de mudar runner.
5. Sugerir comandos de CI compativeis com o runner escolhido.

## Nao fazer

- Nao misturar VSTest e MTP sem decisao explicita.
- Nao criar teste dependente de ordem.
- Nao usar sleeps ou relogio real sem abstracao.

## Saida esperada

- Padrao de testes.
- Ajustes por projeto.
- Comandos `dotnet test`.
- Riscos de migracao do runner.
