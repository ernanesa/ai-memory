---
name: dotnet10-migration-master-plan
description: 'Plano mestre para migracao corporativa .NET 10. Use quando precisar planejar ondas de migracao, ordem dos sistemas, riscos, rollback e criterio de pronto.'
argument-hint: 'portfolio, sistema ou grupo de projetos'
user-invocable: true
---

# Plano Mestre de Migracao .NET 10

## Objetivo

Criar um plano de migracao seguro, incremental e verificavel para levar sistemas da empresa para .NET 10.

## Fluxo obrigatorio

1. Inventariar sistemas, owners, TFMs, SDKs, pacotes, bancos, jobs, APIs, mensageria e criticidade.
2. Classificar risco por runtime, build, contrato publico, persistencia, seguranca, performance, deploy e cobertura de testes.
3. Organizar ondas pequenas, com PRs reversiveis e criterios de aceite.
4. Separar migracao obrigatoria de modernizacao opcional.
5. Definir rollback, smoke tests e sinais de producao.

## Nao fazer

- Nao recomendar big bang sem justificativa.
- Nao misturar limpeza estetica com migracao de framework.
- Nao declarar compatibilidade sem build/teste/evidencia.

## Saida esperada

- Onda sugerida.
- Sistemas incluidos.
- Riscos.
- Checklist de aceite.
- Comandos de validacao.
- Proximos PRs.
