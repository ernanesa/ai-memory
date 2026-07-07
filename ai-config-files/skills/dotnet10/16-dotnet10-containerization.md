---
name: dotnet10-containerization
description: 'Containerizacao de aplicacoes .NET 10. Use para revisar Dockerfile, imagens oficiais, publish, health checks, usuario nao-root e tamanho de imagem.'
argument-hint: 'Dockerfile, servico, worker ou API'
user-invocable: true
---

# Containerizacao .NET 10

## Objetivo

Criar imagens previsiveis, seguras e pequenas o bastante sem remover diagnostico essencial.

## Fluxo obrigatorio

1. Revisar Dockerfile, contexto de build e imagem base.
2. Preferir multi-stage build e runtime enxuto.
3. Rodar como usuario nao-root quando possivel.
4. Configurar portas, env vars, health checks e logs em stdout/stderr.
5. Validar publish, startup e smoke test.

## Nao fazer

- Nao copiar fonte inteiro para imagem final.
- Nao incluir secrets em imagem.
- Nao reduzir tamanho removendo ferramentas criticas de diagnostico sem alternativa.

## Saida esperada

- Dockerfile recomendado.
- Riscos.
- Comandos de build/run.
- Checklist de deploy.
