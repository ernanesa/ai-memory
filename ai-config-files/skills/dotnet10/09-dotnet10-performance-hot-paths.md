---
name: dotnet10-performance-hot-paths
description: 'Revisao de hot paths e performance em .NET 10. Use para analisar codigo critico antes/depois da migracao, procurando allocations, LINQ caro, async indevido e gargalos.'
argument-hint: 'metodo, endpoint, job ou fluxo critico'
user-invocable: true
---

# Performance e Hot Paths .NET 10

## Objetivo

Melhorar performance com medicao, sem transformar o codigo em um labirinto cromado.

## Fluxo obrigatorio

1. Identificar caminho quente, volume, SLA e metrica de sucesso.
2. Procurar allocations, boxing, reflection, LINQ em loop, logging caro e serializacao custosa.
3. Revisar EF Core, HttpClient, locks, async/await e pooling.
4. Medir antes de otimizar.
5. Propor mudancas pequenas e benchmarkaveis.

## Nao fazer

- Nao otimizar no escuro.
- Nao sacrificar legibilidade por ganho nao medido.
- Nao tratar gargalo de banco/rede como problema de CPU.

## Saida esperada

- Hot path identificado.
- Hipoteses de gargalo.
- Medicoes necessarias.
- Plano de benchmark ou profiling.
