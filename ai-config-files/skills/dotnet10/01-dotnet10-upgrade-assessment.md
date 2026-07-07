---
name: dotnet10-upgrade-assessment
description: 'Assessment inicial de projeto antes da migracao para .NET 10. Use para descobrir o que precisa mudar antes de atualizar TargetFramework.'
argument-hint: 'path do projeto, solucao ou erro de upgrade'
user-invocable: true
---

# Assessment de Upgrade para .NET 10

## Objetivo

Diagnosticar o estado atual de um projeto antes de qualquer alteracao de framework.

## Fluxo obrigatorio

1. Ler `.sln`, `.csproj`, `global.json`, `Directory.Build.props` e `Directory.Packages.props`.
2. Mapear TargetFramework, SDK, pacotes, analyzers, nullable e warnings relevantes.
3. Procurar APIs obsoletas, dependencias sem suporte e testes ausentes.
4. Separar bloqueadores reais de melhorias desejaveis.
5. Propor plano em PRs pequenos.

## Nao fazer

- Nao atualizar codigo antes do diagnostico.
- Nao assumir que pacote antigo suporta `net10.0` sem evidência.
- Nao remover multi-target sem entender consumidores.

## Saida esperada

- Resumo do estado atual.
- Lista de riscos.
- Ordem de migracao.
- Arquivos envolvidos.
- Validacoes recomendadas.
