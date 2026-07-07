---
name: dotnet10-observability
description: 'Observabilidade para sistemas .NET 10 em producao. Use para revisar logs estruturados, metricas, tracing, health checks e diagnostico pos-migracao.'
argument-hint: 'servico, API, worker ou fluxo produtivo'
user-invocable: true
---

# Observabilidade .NET 10

## Objetivo

Garantir que a migracao seja visivel, diagnosticavel e segura em producao.

## Fluxo obrigatorio

1. Revisar logs estruturados, correlation id e contexto de erro.
2. Mapear metricas tecnicas e de negocio.
3. Checar traces entre API, banco, mensageria e servicos externos.
4. Adicionar health checks uteis sem vazar detalhes internos.
5. Definir sinais de sucesso da migracao em producao.

## Nao fazer

- Nao logar segredo, token ou dado sensivel.
- Nao criar metrica sem uso operacional.
- Nao deixar observabilidade para depois do deploy.

## Saida esperada

- Sinais de monitoramento.
- Logs esperados.
- Metricas.
- Alertas.
- Smoke test produtivo.
