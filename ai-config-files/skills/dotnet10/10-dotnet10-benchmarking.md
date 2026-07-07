---
name: dotnet10-benchmarking
description: 'Benchmark com BenchmarkDotNet para validar ganhos reais. Use quando precisar comparar baseline vs .NET 10, algoritmo, serializacao ou hot path CPU-bound.'
argument-hint: 'cenario, metodo ou comparacao de performance'
user-invocable: true
---

# Benchmark .NET 10

## Objetivo

Criar benchmarks confiaveis para validar ganhos reais e evitar teatro de performance.

## Fluxo obrigatorio

1. Definir baseline, hipotese e variavel medida.
2. Isolar CPU/memoria de I/O externo.
3. Rodar em Release e ambiente estavel.
4. Comparar media, desvio, alocacoes e regressao.
5. Registrar ambiente e interpretar resultado com cautela.

## Nao fazer

- Nao usar Stopwatch como prova final.
- Nao benchmarkar rede/banco externo como se fosse CPU puro.
- Nao aceitar ganho pequeno se a complexidade explodir.

## Saida esperada

- Codigo de benchmark sugerido.
- Baseline.
- Metricas.
- Como executar.
- Como interpretar.
