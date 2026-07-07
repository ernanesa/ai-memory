# Workflow: onda de migracao .NET 10

1. Inventariar sistemas, owners, SLAs, jobs, APIs, bancos e integracoes.
2. Classificar risco: baixo, medio, alto.
3. Escolher uma onda pequena.
4. Abrir PR de preparacao: CI, testes, warnings e dependencias.
5. Abrir PR de TargetFramework/SDK.
6. Corrigir build sem refatoracao ampla.
7. Rodar testes e smoke tests.
8. Validar contratos e observabilidade.
9. Fazer deploy controlado.
10. Registrar licoes e avancar para proxima onda.

## Criterio de pronto

- Build limpo.
- Testes criticos verdes.
- Contratos preservados ou versionados.
- Rollback documentado.
- Monitoramento ativo.
