---
name: dotnet10-code-style-analyzers
description: 'Padronizacao de estilo, analyzers e manutencao de codigo em .NET 10. Use para configurar editorconfig, analyzers, nullable e warnings de forma sustentavel.'
argument-hint: 'repo, solucao, editorconfig ou conjunto de warnings'
user-invocable: true
---

# Code Style e Analyzers .NET 10

## Objetivo

Elevar qualidade sem transformar a migracao em guerra civil de espacos, nomes e preferencias.

## Fluxo obrigatorio

1. Ler estilo atual antes de propor regra nova.
2. Centralizar regras em `.editorconfig` e `Directory.Build.props`.
3. Separar regras obrigatorias de sugestoes.
4. Subir severidade gradualmente quando houver divida.
5. Evitar reformatar tudo junto com upgrade.

## Nao fazer

- Nao reformatar o repositorio inteiro em PR de migracao.
- Nao ativar regra conflitante com padrao dominante.
- Nao usar analyzer como substituto de code review.

## Saida esperada

- Regras recomendadas.
- Severidade.
- Plano incremental.
- Comandos de validacao.
