# Definition of Done (DoD)

Um item está **Done** quando atende a todos os critérios abaixo. Itens não-Done não são contabilizados em velocity nem entrarem em release.

## Critérios de código

- [ ] Código implementado e commitado em branch
- [ ] PR aberto, revisado e aprovado
- [ ] Squash-merge em `main`
- [ ] Sem warnings novos (`dotnet build` limpo)
- [ ] Sem `Console.WriteLine` em serviços chamados pelo MCP (stdout corruption)
- [ ] Sem comentários explicando "o quê" (só "por quê" não-óbvio é permitido)
- [ ] Segue estilo do codebase (file-scoped namespaces, nullable enabled, etc.)

## Critérios de teste

- [ ] Testes unitários para lógica nova
- [ ] Testes de integração para SQL/repositório novo (Testcontainers/Postgres)
- [ ] Testes regressivos para bug fix (teste que falha antes do fix)
- [ ] Cobertura de serviços core não diminuiu
- [ ] `dotnet test` verde localmente

## Critérios de build/release

- [ ] `dotnet build -c Release` verde
- [ ] `dotnet pack -c Release` gera nupkg sem erro
- [ ] Sem dependências novas sem ADR
- [ ] Versão do package bumpada se aplicável (SemVer)

## Critérios de documentação

- [ ] README atualizado se comportamento de usuário mudou
- [ ] ADR escrito se decisão técnica foi tomada
- [ ] Changelog atualizado (RELEASE_NOTES ou seção do README)
- [ ] Documentação de API/MCP atualizada se endpoint/tool mudou

## Critérios de aceitação

- [ ] Todos os critérios de aceitação da user story verificados
- [ ] PO aceitou o incremento (implícito para single-contributor: auto-aceitação com rationale)
- [ ] Persona beneficiada identificada (post-mortem mental)

## Critérios de operação

- [ ] Sem migrations SQL destrutivas sem script de rollback
- [ ] Sem breaking changes sem ADR e bump de major version
- [ ] Setup (`ai-memory setup`) testado se schema mudou

## DoD para hotfixes (caminho acelerado)

- [ ] Build verde
- [ ] Teste regressivo para o bug
- [ ] PR com label `hotfix`
- [ ] Changelog atualizado
- Demais critérios podem ser pós-aplicados em tech debt

## Não é Done

- "Funciona na minha máquina"
- "Falta só escrever teste"
- "Falta documentar"
- "Falta bumpar versão"
- "Vou limpar warnings depois"
- "PR aberto mas não mergado"
