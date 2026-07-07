# Checklist de PR para migracao .NET 10

- TargetFramework/SDK alterado de forma explicita.
- Build Release passa.
- Testes relevantes passam.
- Warnings novos foram avaliados.
- Dependencias criticas foram verificadas.
- Contratos HTTP/OpenAPI foram preservados ou versionados.
- Mudancas de EF Core/migrations foram revisadas.
- Logs e metricas continuam uteis.
- Seguranca nao foi relaxada.
- Rollback esta claro.

## Regra de ouro

PR de migracao deve ser pequeno, auditavel e reversivel. Refatoracao grande entra em PR separado.
