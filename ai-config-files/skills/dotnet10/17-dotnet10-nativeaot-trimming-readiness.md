---
name: dotnet10-nativeaot-trimming-readiness
description: 'Prontidao para trimming e NativeAOT em .NET 10. Use para avaliar se uma aplicacao ou biblioteca pode usar trimming/AOT com seguranca.'
argument-hint: 'projeto, biblioteca, worker ou console app'
user-invocable: true
---

# Prontidao para Trimming e NativeAOT

## Objetivo

Avaliar AOT/trimming com prudencia, sem transformar publish em roleta russa de runtime.

## Fluxo obrigatorio

1. Identificar reflection, dynamic, serializers, DI scanning, plugins e carregamento dinamico.
2. Rodar publish com warnings de trimming/AOT visiveis.
3. Classificar warnings em bloqueadores, aceitaveis e dependentes de teste.
4. Comecar por console/worker/libs antes de APIs complexas.
5. Registrar fallback quando AOT nao compensar.

## Nao fazer

- Nao habilitar NativeAOT por moda.
- Nao ignorar warnings de trimming.
- Nao prometer compatibilidade sem publish e smoke test.

## Saida esperada

- Score de prontidao.
- Warnings esperados.
- Ajustes necessarios.
- Decisao: usar, adiar ou descartar.
