---
name: dotnet10-release-workflow
description: 'Workflow de release para aplicacoes e pacotes .NET 10. Use para padronizar versionamento, tags, changelog, artefatos, NuGet, containers e rollback.'
argument-hint: 'tipo de release, pacote, app ou repositorio'
user-invocable: true
---

# Release Workflow .NET 10

## Objetivo

Padronizar releases de sistemas .NET 10 com rastreabilidade e rollback claro.

## Fluxo obrigatorio

1. Definir origem da versao: tag, arquivo, GitVersion, Nerdbank ou manual.
2. Gerar build reproduzivel em Release.
3. Rodar testes antes de publicar artifacts.
4. Publicar pacote, app ou imagem com metadados rastreaveis.
5. Documentar smoke test e rollback.

## Nao fazer

- Nao publicar pacote sem changelog minimo.
- Nao misturar release manual e automatico sem regra.
- Nao deletar artefatos necessarios para auditoria.

## Saida esperada

- Modelo de release.
- Workflow sugerido.
- Checklist.
- Rollback.
