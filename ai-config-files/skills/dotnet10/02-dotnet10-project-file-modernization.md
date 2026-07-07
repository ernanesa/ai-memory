---
name: dotnet10-project-file-modernization
description: 'Padronizacao de csproj e arquivos MSBuild para .NET 10. Use ao revisar TargetFramework, nullable, implicit usings, analyzers e central package management.'
argument-hint: 'csproj, props, packages props ou padrao desejado'
user-invocable: true
---

# Modernizacao de Arquivos de Projeto .NET 10

## Objetivo

Padronizar projetos .NET 10 sem criar divergencias entre sistemas da empresa.

## Fluxo obrigatorio

1. Ler `.csproj`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` e `global.json`.
2. Identificar duplicacoes de propriedades e versoes de pacotes.
3. Recomendar padroes centralizados para `TargetFramework`, `Nullable`, `ImplicitUsings`, `AnalysisLevel` e warnings.
4. Preservar propriedades especificas de apps, libs, workers, tests e tools.
5. Sugerir diff pequeno e reversivel.

## Nao fazer

- Nao ativar `TreatWarningsAsErrors` em massa sem plano.
- Nao forcar `LangVersion` preview sem motivo.
- Nao apagar propriedades legadas sem entender o build.

## Saida esperada

- Padrao recomendado.
- Arquivos a alterar.
- Riscos.
- Diff conceitual.
- Validacao com `dotnet restore`, `dotnet build` e `dotnet test`.
