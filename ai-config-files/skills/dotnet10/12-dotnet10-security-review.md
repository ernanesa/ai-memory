---
name: dotnet10-security-review
description: 'Revisao de seguranca para migracao .NET 10. Use para revisar secrets, autenticacao, autorizacao, CORS, headers, dependencias vulneraveis e logging sensivel.'
argument-hint: 'sistema, endpoint, configuracao ou dependencia'
user-invocable: true
---

# Revisao de Seguranca .NET 10

## Objetivo

Evitar que a migracao abra portas que estavam fechadas por acidente.

## Fluxo obrigatorio

1. Verificar secrets fora do codigo e de appsettings versionado.
2. Revisar autenticacao, autorizacao, policies, claims e endpoints anonimos.
3. Checar CORS, cookies, tokens, HTTPS, headers e redirects.
4. Procurar SQL injection, SSRF, path traversal e logs sensiveis.
5. Recomendar atualizacao de pacotes vulneraveis e validacao automatizada.

## Nao fazer

- Nao enfraquecer seguranca para resolver build.
- Nao expor stack trace ou detalhes internos ao usuario final.
- Nao criar bypass temporario sem controle e remocao planejada.

## Saida esperada

- Riscos.
- Evidencias.
- Mitigacoes.
- Testes de seguranca.
- Itens bloqueadores.
