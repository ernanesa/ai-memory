---
name: dotnet10-ci-quality-gates
description: 'Quality gates de CI para migracao .NET 10. Use para desenhar pipeline de restore, build, format, test, coverage, analyzers e artifacts.'
argument-hint: 'repositorio, workflow ou politica de CI'
user-invocable: true
---

# Quality Gates de CI .NET 10

## Objetivo

Criar um portao de qualidade que ajude a migracao sem virar catraca quebrada.

## Fluxo obrigatorio

1. Garantir restore, build Release e testes em escopo correto.
2. Usar lock file quando a empresa exigir builds reproduziveis.
3. Rodar format/analyzers de forma gradual se houver divida grande.
4. Publicar TRX, cobertura e artifacts quando forem uteis.
5. Separar benchmark e scans caros do PR padrao.

## Nao fazer

- Nao deixar CI verde ignorando falha de teste.
- Nao bloquear toda a migracao por analyzers novos sem plano.
- Nao rodar benchmark pesado em todo PR.

## Saida esperada

- Pipeline sugerido.
- Gates obrigatorios.
- Gates opcionais.
- Comandos.
- Riscos de tempo de CI.
