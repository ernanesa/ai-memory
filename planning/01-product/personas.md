# Personas

## P1 — Ernane, Engenheiro .NET Sênior

- **Perfil**: desenvolvedor sênior .NET, usa Rider no Linux, trabalha com múltiplos projetos de pagamento/claps.
- **Contexto**: mantém 12+ projetos; quer que o assistente de IA saiba do domínio sem precisar colar arquivos no chat.
- **Dores hoje**:
  - `search_code` às vezes retorna chunks de arquivos que já foram deletados (chunks órfãos).
  - Toda chamada de `search_code` no Cursor pede confirmação — fricção em sessão longa.
  - Sem testes, não confia em refatorar o `PgVectorService`.
- **O que valoriza**: precisão da busca, economia de tokens, setup simples.
- **Cita**:
  > "Quero perguntar 'onde valida limite de crédito?' e o agente saber sem eu apontar o arquivo."

## P2 — Tech Lead de time .NET

- **Perfil**: líder técnico de um time de 5–10 desenvolvedores .NET corporativo.
- **Contexto**: precisa padronizar contexto de IA entre o time, com workspaces por cliente/produto.
- **Dores hoje**:
  - Heurísticas pt-BR hard-coded (`não pode`, `bloqueado`, `vencido`) — não funciona para times em inglês.
  - Sem CI/CD, releases manuais — não consegue adotar no time com segurança.
  - Migrations SQL instáveis (`001_*` duplicados) — setup não reproduzível entre máquinas.
- **O que valoriza**: reprodutibilidade, distribuição via NuGet, configuração por time.
- **Cita**:
  > "Preciso que instalar o ai-memory no time seja `dotnet tool install` e pronto, sem chamado de suporte."

## P3 — Agente de IA dentro do IDE (consumidor indireto)

- **Perfil**: Claude/Codex/Cursor rodando dentro do IDE, consumindo o MCP do ai-memory.
- **Contexto**: faz `tools/call` para `search_code`, `find_related_files`, `get_symbol_callers` antes de responder.
- **Dores hoje**:
  - Recebe JSON dentro de `content[].text` — não consegue renderizar estruturado (sem Structured Output).
  - Erros de validação de input viram protocol error e o agente para (sem Tool Execution Error).
  - Sem `readOnlyHint`, pede autorização ao usuário a cada chamada.
- **O que valoriza**: structured output, tool annotations, ícones para identificação.
- **Cita**:
  > "Se eu soubesse que `search_code` é read-only, não interromperia o usuário para confirmar."

## P4 — DevOps / Release Manager

- **Perfil**: responsável por empacotar e distribuir a tool via NuGet para o time/empresa.
- **Contexto**: precisa de pipeline reproduzível, versionamento semântico, release notes automáticos.
- **Dores hoje**:
  - Sem CI/CD — `dotnet pack` manual, propenso a erro.
  - Tray embutido no package principal — binário grande, atualizações desacopladas impossíveis.
  - Build quebra em algumas máquinas (problema do `SetUnixFileMode`).
- **O que valoriza**: pipeline automatizado, packages separados, build verde.
- **Cita**:
  > "Hoje soltar versão é ritual manual de 30 minutos. Quero `git tag v0.4.0` e pronto."

## P5 — Comunidade .NET open-source

- **Perfil**: desenvolvedor .NET que descobre o projeto no GitHub/NuGet.
- **Contexto**: avalia se vale adotar, contribuir, ou forkar.
- **Dores hoje**:
  - README extenso mas setup complexo (3 dependências: Postgres + Ollama + baixar modelos).
  - Sem testes — desconfia de qualidade.
  - Sem releases tags no GitHub — não sabe o que é estável.
- **O que valoriza**: documentação clara, releases versionadas, testes como prova de qualidade.
