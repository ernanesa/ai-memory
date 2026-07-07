---
name: dotnet10-legacy-system-strangler-plan
description: 'Plano Strangler Fig para sistemas legados migrando para .NET 10. Use quando reescrita completa for arriscada e for melhor migrar por bordas, modulos ou fluxos.'
argument-hint: 'sistema legado, modulo, fluxo ou fronteira'
user-invocable: true
---

# Plano Strangler para Legados .NET 10

## Objetivo

Migrar sistemas legados por fatias seguras, mantendo producao viva enquanto o novo cresce em volta do antigo.

## Fluxo obrigatorio

1. Identificar fronteiras: API, mensageria, banco, jobs, UI e integracoes.
2. Escolher fatias verticais pequenas com valor e baixo risco.
3. Definir coexistencia entre legado e novo com roteamento claro.
4. Monitorar os dois caminhos durante transicao.
5. Criar criterio para desligar partes antigas.

## Nao fazer

- Nao recomendar reescrita total sem analise economica.
- Nao duplicar regra de negocio sem fonte de verdade.
- Nao manter dois caminhos produtivos sem observabilidade.

## Saida esperada

- Fronteiras.
- Primeira fatia.
- Plano de coexistencia.
- Riscos.
- Criterio de desligamento.
