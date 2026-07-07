---
name: csharp14-modernization
description: 'Modernizacao cuidadosa para C# 14 em projetos .NET 10. Use para avaliar recursos novos sem sacrificar clareza ou padrao do time.'
argument-hint: 'classe, metodo, modulo ou padrao de codigo'
user-invocable: true
---

# Modernizacao C# 14

## Objetivo

Usar C# 14 apenas quando houver ganho real de clareza, seguranca, manutencao ou performance.

## Fluxo obrigatorio

1. Identificar o padrao atual do projeto.
2. Avaliar recursos como field-backed properties, extension blocks, nameof com genericos, spans e null-conditional assignment.
3. Evitar novidade por novidade.
4. Preservar comportamento observavel.
5. Propor mudancas pequenas e validaveis.

## Nao fazer

- Nao trocar codigo estavel por recurso novo sem ganho claro.
- Nao introduzir padrao que o time ainda nao usa sem plano.
- Nao vender micro-otimizacao sem medicao.

## Saida esperada

- O que modernizar.
- O que manter como esta.
- Risco de legibilidade.
- Validacao sugerida.
