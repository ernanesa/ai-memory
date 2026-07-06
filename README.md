# AI Memory

**Memória local de engenharia para agentes de IA.** Indexa código, regras de negócio e conhecimento técnico dos seus projetos .NET e expõe tudo via **MCP (Model Context Protocol)** sobre STDIO para Rider, VS Code, Cursor, Codex, Claude Desktop, Antigravity, opencode — sem mandar seu código para a nuvem e reduzindo drasticamente os tokens gastos em cada resposta.

> Stack: **.NET 10 + Ollama (bge-m3) + PostgreSQL/pgvector + Roslyn + Avalonia + MCP 2025-11-25**.

---

## Para que serve

Agentes de IA dentro das IDEs (Rider, VS Code, Cursor, Codex, Claude Desktop, etc.) respondem melhor quando conhecem o seu código — mas reenviar arquivos inteiros a cada pergunta custa tokens, latência e contexto. O `ai-memory` resolve isso mantendo uma **memória local pesquisável** que o agente consulta antes de responder:

```text
modelo LLM       = raciocina
bge-m3           = transforma texto em vetor (embedding)
pgvector         = encontra contexto semanticamente parecido
Roslyn           = entende a estrutura do C# (símbolos, hierarquia, chamadas)
ai-memory        = mantém tudo atualizado (index, watch, tray, dashboard)
MCP (STDIO)      = conecta essa memória ao agente dentro da IDE
```

Com isso um agente consegue, por exemplo:

- responder "onde valida limite de crédito?" lendo 1 resultado semântico em vez de varrer o repositório;
- descobrir quem chama um método (`get_symbol_callers`) antes de refatorar;
- conhecer regras de negócio (`search_business_rules`) extraídas do próprio código;
- conhecer decisões arquiteturais (`search_knowledge`) sem reabrir ADRs.

Tudo roda **100% local** — nenhum fonte, embedding ou prompt sai da sua máquina, exceto para o Ollama que normalmente também é local.

---

## Arquitetura

```text
Rider / VS Code / Cursor / Codex / Claude / opencode / Antigravity
                       │  (MCP over STDIO — protocol 2025-11-25)
                       ▼
                ai-memory mcp   ← 7 ferramentas só-leitura
                       │
        ┌──────────────┼───────────────────┐
        ▼              ▼                    ▼
   PostgreSQL     Ollama (bge-m3)      Roslyn (AST C#)
   + pgvector     qwen2.5-coder:7b     grafo de símbolos
   (RRF híbrido)  (embeddings/LLM)    (calls/inherits/implements)
        ▲
        │  upsert / search / graph
        │
   ai-memory index         ai-memory watch         ai-memory tray
   (chunks→rules→knowledge) (FileSystemWatcher)    (Avalonia, autostart)
        ▲
        │
   Projetos .NET locais (workspaces multi-projeto)
```

Componentes principais:

| Componente | Localização | Responsabilidade |
|---|---|---|
| CLI `ai-memory` | `ai-memory.csproj` → NuGet `AiMemory.Tool` | Setup, indexação, busca, dashboard, MCP, gerência da bandeja |
| Tray `ai-memory-tray` | `AiMemory.Tray.csproj` → NuGet `AiMemory.Tray` | Monitor visual na bandeja do sistema (Avalonia) |
| Config | `Configuration/` | `~/.ai-memory/config.json` + `~/.aimemory/patterns.json` |
| SQL | `sql/000_schema.sql`, `010_compat.sql`, `020_hybrid_search.sql` | Schema, migrações compatíveis, busca híbrida (FTS + vetorial) |
| Serviços | `Services/` (20 arquivos) | Chunking, repositórios, extração, reranking, compressão, ícone da bandeja, etc. |
| Comandos | `Commands/` | `index`, `search`, `watch`, `doctor`, `setup`, `workspace`, `project`, `mcp`, `dashboard`, `tray` |
| Testes | `tests/AiMemory.Tests/` | 36 unit + 5 integração (xUnit + FluentAssertions + Testcontainers) |
| CI/CD | `.github/workflows/ci.yml`, `release.yml` | Build/teste 3 OS + publish NuGet em tags `v*` |

---

## Instalação, atualização e remoção

O `ai-memory` é distribuído como **.NET global tool** com `PackageId = AiMemory.Tool` e comando `ai-memory`. A bandeja é um **segundo pacote** (`AiMemory.Tray`, comando `ai-memory-tray`) para não arrastar dependências Avalonia para a CLI/MCP.

### Pré-requisitos

- **.NET SDK 10.0.x** — instalador oficial em <https://dotnet.microsoft.com>.
- **PostgreSQL 14+** com extensão **pgvector** (instalada automaticamente pelo `setup`).
- **Ollama** rodando localmente (com `bge-m3` para embeddings e, opcionalmente, `qwen2.5-coder:7b` para extração semântica).
- Para os testes de integração: **Docker** (Testcontainers sobe `pgvector/pgvector:pg17`).

### Instalar a partir do pacote local (build do repo)

```bash
git clone <este-repo> && cd ai-memory
dotnet pack ai-memory.csproj -c Release
dotnet tool install --global --add-source ./bin/Release AiMemory.Tool
```

### Instalar uma versão específica de um feed NuGet

```bash
dotnet tool install --global AiMemory.Tool --version 0.2.0 --add-source <feed-nuget-da-empresa>
```

### Atualizar a versão instalada

```bash
dotnet tool update --global AiMemory.Tool --version 0.2.0 --add-source <feed-nuget-da-empresa>
ai-memory tray update   # recria o autostart apontando para o shim atual
```

### Remover a tool

```bash
ai-memory tray remove               # remove autostart da bandeja
dotnet tool uninstall --global AiMemory.Tool
dotnet tool uninstall --global AiMemory.Tray   # se a bandeja estiver instalada
```

> Rode `ai-memory tray update` depois de qualquer `dotnet tool update` para recriar o atalho de autostart apontando para o shim/executável atual.

---

## Setup (primeira vez)

Depois de instalar a tool:

```bash
ai-memory setup
```

O `setup` é um wizard interativo que:

1. **Coleta** todas as respostas do usuário: banco, host, porta, usuário/senha opcionais do PostgreSQL, URL do Ollama, modelo de embedding, modelo semântico, workspaces, projetos e ações automáticas permitidas.
2. **Executa** o plano até o fim sem novas perguntas.

Na etapa de **workspaces**, informe um nome (ex: `claps` ou `pagueOn`). Para cada workspace, informe um diretório de projeto por vez — o nome do projeto é inferido pelo nome da pasta. Pressione **Enter** vazio para fechar o workspace; depois o setup pergunta se deseja configurar outro workspace.

A interface usa **cores** para destacar estados, perguntas, avisos e sucesso. Logs de instalação e comandos externos são compactados (apenas últimas linhas relevantes), mantendo a interação legível — a única exceção é `ollama pull`, exibido em tempo real. Ao final, exibe um resumo colorido do que foi concluído, ignorado ou ficou pendente.

### O que o setup automatiza (conforme a plataforma)

| Plataforma | Dependências | Início dos serviços |
|---|---|---|
| **macOS** | `brew install postgresql`, `pgvector`, `ollama` | `brew services start ...` |
| **Ubuntu/Debian** | `apt install postgresql`, `postgresql-contrib`, `postgresql-{ver}-pgvector` (fallback build local do pgvector v0.8.2); `curl install.sh \| sh` para Ollama | `systemctl start postgresql` / `ollama` |
| **Windows 11** | `winget install PostgreSQL.PostgreSQL.18` e `Ollama.Ollama` (instaladores interativos); build do pgvector via `nmake` se houver VS Build Tools | serviço Windows do PostgreSQL + `ollama serve` em background |

Comum a todas as plataformas:

- cria o banco `ai_memory` via Npgsql (sem depender do comando `createdb`);
- aplica o schema SQL (`sql/*.sql`);
- lista modelos disponíveis no Ollama;
- baixa os modelos escolhidos (padrão `bge-m3` + `qwen2.5-coder:7b`);
- instala e habilita o autostart da bandeja (se houver sessão gráfica).

### Observações por plataforma

- **macOS**: usuário padrão do PostgreSQL costuma ser o usuário atual do sistema.
- **Ubuntu/Windows**: setup sugere `postgres` como padrão.
- Host e porta do PostgreSQL são **opcionais** (default `localhost` / `5432`).
- **Ubuntu**: `pgvector` é tentado pelo pacote compatível com a versão do PostgreSQL, com fallback para build local.
- **Windows**: `pgvector` pode exigir instalação manual no servidor se as build tools do Visual Studio não estiverem disponíveis no terminal atual.

Se o gerenciador de pacotes, `pgvector` ou algum serviço não puderem ser automatizados, o setup mostra o que faltou e pode ser **re-executado** depois do ajuste manual.

---

## Uso

### Workflow recomendado

```bash
ai-memory setup                 # primeira vez
ai-memory index                 # chunks -> rules -> knowledge
ai-memory mcp                   # deixa o servidor MCP rodando para as IDEs
# (ou adicione a config de MCP da sua IDE e ela começará sozinha)
ai-memory watch                 # opcional: reindexa ao salvar arquivos
ai-memory tray                  # opcional: abre a bandeja do sistema
ai-memory dashboard serve       # opcional: dashboard web em http://localhost:5050
```

### Indexação (pipeline em fases)

`ai-memory index` executa por padrão o pipeline completo:

```text
chunks -> rules -> knowledge
```

Fases:

- **`chunks`** — lê os arquivos dos projetos configurados, quebra em chunks via **Roslyn** (C#), regex (SQL), headers (Markdown) ou size-split (JSON/config), gera embeddings via Ollama `bge-m3` e grava em `ai_chunks`. Também constrói o **grafo de símbolos** Roslyn (classes, interfaces, métodos, herança, chamadas) em `ai_symbols` / `ai_symbol_relations`.
- **`rules`** — usa chunks já indexados para extrair/reconciliar **regras de negócio** heurísticas (ou semânticas com `--semantic`) e gravar em `ai_business_rules`. Sinais considerados: exceções de domínio, FluentValidation, padrões `ErroContext` / `AddFailure` / `RuleFor`, termos pt-BR como *bloqueado*, *cancelado*, *vencido*, *elegível*, *permite*.
- **`knowledge`** — usa chunks já indexados para extrair/reconciliar **conhecimento técnico** (integrações, padrões, riscos técnicos, arquitetura, configuração) em `ai_knowledge`. Sinais: `HttpClient`, `MassTransit`/`RabbitMQ`/`Kafka`, `MediatR`, `Entity Framework`, `TODO`/`FIXME`/`HACK`.

As fases `rules` e `knowledge` são **incrementais por `content_hash`**: chunks já processados com o mesmo hash são ignorados. Se o conteúdo mudar (reindexação ou `watch`), o chunk volta a ser candidato. Candidatos são sempre gravados como `candidate` — **nunca** são auto-promovidos para `accepted` (isso é revisão humana explícita). Regras `rejected` não são reativadas automaticamente.

```bash
ai-memory index                          # pipeline completo
ai-memory index chunks                   # só chunks
ai-memory index rules                    # só rules
ai-memory index knowledge                # só knowledge
ai-memory index chunks rules             # fases selecionadas
ai-memory index rules knowledge --candidate-limit 2000

# escopo por projeto/workspace
ai-memory index --project gestor --workspace claps
ai-memory index chunks --project gestor --workspace claps

# extração semântica (mais lenta, usa LLM)
ai-memory index rules knowledge --semantic
ai-memory index rules knowledge --semantic --semantic-model qwen2.5-coder:7b --candidate-limit 500
ai-memory index rules knowledge --semantic --refresh --candidate-limit 500

# controle de paralelismo dos embeddings
ai-memory index --parallelism 4
```

**Sobre as flags de `index`:**

| Flag | Default | Descrição |
|---|---|---|
| `--workspace <nome>` | workspace ativo | Restringe a um workspace |
| `--project <nome>` | todos do workspace | Restringe a um projeto (suporta filtro por nome) |
| `--candidate-limit <n>` | sem limite | Máximo de chunks considerados por `rules`/`knowledge` |
| `--semantic` | off | Usa extração semântica nas fases `rules`/`knowledge` (LLM) |
| `--semantic-model <id>` | `qwen2.5-coder:7b` ou env `AI_MEMORY_SEMANTIC_MODEL` | Modelo Ollama usado pela extração semântica |
| `--refresh` | off | Revisaita TODOS os chunks do escopo, mesmo os já `processed` |
| `--parallelism <n>` | auto: chunks `Clamp(cpu/2, 2, 6)`; rules/knowledge `4` | Concorrência máxima de embedding/upsert |
| `--db <...>` | config | Nome do banco ou connection string completa |
| `--ollama <url>` | config | URL base do Ollama |
| `--model <id>` | config | Modelo de embedding Ollama |

> Sintaxe antiga `ai-memory index gestor --workspace claps` ainda funciona (deprecated) e emite aviso para usar `--project gestor`.

**Sobre o progresso:** durante `chunks`/`rules`/`knowledge`, a tool mostra um **painel de progresso de 4 linhas** em terminais interativos (fase, %, decorrido, ETA suavizado, contadores, arquivo/símbolo atual, taxa média), atualizado a cada 1s. Em pipe/CI/redirect ela cai para logs lineares. O ETA é arredondado (`eta ~11h45m`) porque chamadas de LLM variam entre chunks. Quando `--candidate-limit` é omitido e há candidatos, exibe um aviso amarelo sugerindo o uso do limit.

**Exclusões automáticas** (em `chunks`, `rules` e `knowledge`):

- Migrations geradas pelo **Entity Framework** (detectadas por conteúdo: `: Migration`, `: ModelSnapshot`, `[Migration(`, `[DbContext(`, `MigrationBuilder`, `BuildTargetModel`). Chunks antigos identificados como migrations também são removidos ao reindexar.
- Código de **teste**: projetos com `Microsoft.NET.Test.Sdk`, `xunit`, `NUnit`, `MSTest`, `<IsTestProject>true</IsTestProject>`, `coverlet.collector`, e arquivos C# com `[Fact]`/`[Theory]`/`[Test]`/`[TestMethod]`/`[TestClass]`/`[TestFixture]`/`[SetUp]`, ou caminhos/pastas comuns (`Tests`, `UnitTests`, `IntegrationTests`, `Specs`). Chunks antigos de teste também são removidos ao reindexar.
- Diretórios ignorados: `.git`, `bin`, `obj`, `node_modules`, `dist`, `coverage`, `packages`, `.idea`, `.vs`, `.vscode`. Dentro de um repo Git, o `git ls-files --cached --others --exclude-standard` respeita `.gitignore`, `.git/info/exclude` e excludes do Git.

### Busca

```bash
ai-memory search "onde valida limite de crédito?"
ai-memory search "DbContext" --limit 20
```

| Flag | Default | Descrição |
|---|---|---|
| `<query>` (posicional, obrigatório) | — | Texto da busca |
| `--limit <n>` | 10 | Máximo de resultados |
| `--db`, `--ollama`, `--model` | config | Overrides |

A busca é **híbrida**: usa HNSW vetorial (`embedding <=> $1`) + `ts_rank_cd(websearch_to_tsquery(...))` fundidos por **Reciprocal Rank Fusion** (`1/(60+rank)`). Depois um **reranker heurístico** promove matches exatos de símbolo e penaliza arquivos de config/docs em queries estruturais.

### Watch

```bash
ai-memory watch
```

Um `FileSystemWatcher` (real, debounce de 500ms) reindexa apenas os arquivos alterados em extensões monitoradas (`.cs`, `.csproj`, `.sln`, `.sql`, `.json`, `.md`, `.yml`, `.yaml`, `.config`, `.props`, `.targets`, `.razor`, `.cshtml`). Ignora `bin/`, `obj/`, `.git/`. Arquivos deletados disparam limpeza de chunks órfãos; renomeações disparam delete+index. Pressione **Ctrl+C** para parar.

### Dashboard

```bash
ai-memory dashboard                                # resumo no terminal
ai-memory dashboard --workspace claps              # resumo de um workspace
ai-memory dashboard --project clapsapi             # resumo de um projeto
ai-memory dashboard serve                          # web UI em http://localhost:5050
ai-memory dashboard serve --port 8080
ai-memory dashboard serve --workspace claps --project clapsapi
```

`dashboard serve` abre uma SPA local (tabs: Overview, Projects, Chunks, Business Rules, Knowledge, Health) com endpoints JSON `/api/overview`, `/api/workspaces`, `/api/projects`, `/api/chunks`, `/api/business-rules`, `/api/knowledge`, `/api/health`. Filtros por query string: `workspace`, `project`, `q`, `limit`.

### Workspace e Project

```bash
ai-memory workspace list
ai-memory workspace add claps
ai-memory workspace use claps
ai-memory workspace remove claps

ai-memory project add --workspace claps            # pede o diretório interativamente
ai-memory project list --workspace claps
ai-memory project remove gestor --workspace claps
```

Um **workspace** é um recorte de trabalho (cliente/produto/contexto). Um **projeto** é identificado pelo `root_path` e pode participar de mais de um workspace (relação muitos-para-muitos via `ai_workspace_projects`).

### Doctor (validação do ambiente)

```bash
ai-memory doctor                       # relatório legível
ai-memory doctor --json                # JSON para scripts/CI
ai-memory doctor --strict --no-network # warnings viram falhas; pula rede
```

Verifica: config (incl. permissões `0600`/`0700` e presença de senha em `config.json`), workspaces/projetos (nomes e diretórios), PostgreSQL (`SELECT 1`), extensões obrigatórias (`vector`, `pgcrypto`) e opcionais (`uuid-ossp`), tabelas e colunas esperadas, status do schema (`ai_schema_migrations`), `ai_projects.id` como `integer`, e reachability do Ollama (skip com `--no-network`). Retorna **código 1** se houver falhas (ou warnings em `--strict`).

---

## Servidor MCP (`ai-memory mcp`)

Inicia o servidor **MCP sobre STDIO** (protocolo `2025-11-25`, JSON-RPC 2.0). É essa a ponte que as IDEs usam para consultar a memória do `ai-memory`.

```bash
ai-memory mcp
ai-memory mcp --ollama http://gpu-box:11434 --model bge-m3
```

| Flag | Default | Descrição |
|---|---|---|
| `--db` | config | Nome do banco ou connection string completa |
| `--ollama` | config | URL base do Ollama |
| `--model` | config | Modelo de embedding Ollama |

> O stdout é silenciado durante a execução para não corromper o stream JSON-RPC; logs vão para stderr. As respostas são escritas num stdout reservado capturado no início da sessão.

### Ferramentas expostas (7, todas `readOnlyHint: true`)

| Tool | Argumentos | Retorna |
|---|---|---|
| `search_code` | `query` (obrigatório), `limit?` (1..50, default 10), `project?`, `max_content_chars?` (200..10000, default 1200) | `{Project, File, Language, ChunkType, Symbol, Distance, Content}` (com reranking + compressão de contexto) |
| `search_business_rules` | `query`, `limit?`, `project?` | `{Project, Title, Description, SourceFile, Symbol, Status, Evidence, Confidence, Distance}` |
| `search_knowledge` | `query`, `limit?`, `project?` | `{Project, Kind, Title, Content, Source, Symbol, Status, Evidence, Confidence, Distance}` |
| `find_related_files` | `file?` ou `query?`, `project?`, `limit?` (default 10) | `{Project, File, Distance, MatchedChunks, Symbols}` |
| `get_symbol_callers` | `symbol` (obrigatório), `project?` | `{Project, Symbol, File, Relation}` |
| `get_symbol_callees` | `symbol`, `project?` | `{Project, Symbol, File, Relation}` |
| `get_class_hierarchy` | `className`, `project?` | `{Project, ParentName, Relation}` |

Cada ferramenta declara `annotations.readOnlyHint=true`, `outputSchema` estruturado, ícone SVG embutido e campo `title`. Erros de argumentos viram Tool Execution Errors (`isError: true`); erros de protocolo viram JSON-RPC `-32600`/`-32601`/`-32700`.

A resposta do `initialize` inclui **instructions** recomendando a ordem de uso: `search_code` → `search_business_rules` → `search_knowledge` → `find_related_files` → leitura direta de arquivos só quando insuficiente (preferir `distance < 0.4`).

---

## Configuração de MCP nas IDEs

### Rider
**Settings → Tools → AI Assistant → Model Context Protocol (MCP)**:

```json
{
  "mcpServers": {
    "ai-memory": {
      "command": "ai-memory",
      "args": ["mcp"]
    }
  }
}
```

### VS Code / Cursor / Claude Desktop / Antigravity / Codex / opencode

Há um **script único** que registra o servidor em todos os clientes detectados:

```bash
./setup-mcp.sh
```

Ele escreve/merge `{"command":"ai-memory","args":["mcp"]}` em `~/.config/claude/mcp.json`, `~/.config/Antigravity/User/settings.json` (se existir), `~/.cursor/mcp.json` (se `~/.cursor` existir), e apenas avisa sobre `opencode` (que já usa `~/.config/opencode/opencode.jsonc`). **Idempotente** para Claude/Antigravity (merge via Python); sobrescreve para Cursor.

Configuração genérica (coloque no local esperado pela sua extensão/agente):

```json
{
  "mcpServers": {
    "ai-memory": {
      "command": "ai-memory",
      "args": ["mcp"]
    }
  }
}
```

O projeto já inclui `.opencode/opencode.jsonc` e `.ai/mcp/mcp.json` para uso por opencode e clientes genéricos.

> Para confirmar manualmente que o servidor está respondendo:
> ```bash
> printf '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}\n' | ai-memory mcp
> ```

---

## Tray Application (bandeja do sistema)

Aplicação visual complementar na bandeja do sistema, escrita com **Avalonia 11** no .NET 10 (`AiMemory.Tray`, comando `ai-memory-tray`). Monitora se o servidor MCP está sendo consumido ativamente por alguma IDE — **ícone cinza quando ocioso**, **azul ciano brilhante** quando uma IDE estabelece conexão (verificação a cada 4s enumerando processos `ai-memory`/`dotnet` com `mcp` na linha de comando, sem `tray`).

### Funcionalidades

- **Ícone dinâmico**: cinza (idle) ↔ azul ciano (ativo). Verificado por processo, não por rede.
- **Menu de contexto**:
  - `Indexar Workspace` — dispara `ai-memory index` em background e notifica ao concluir (ou captura stderr em falha).
  - `Testar Banco de Dados` — ping assíncrono no PostgreSQL configurado.
  - `Gerenciar PostgreSQL` — Iniciar/Parar/Reiniciar serviço (`systemctl`/`brew services`/`net`+UAC) e **Criar Banco e Aplicar Migrações** (bootstrap do `ai_memory` + execução de `sql/*.sql`).
  - Workspace switcher dinâmico (submenus listando workspaces, marcando o ativo com ✓).
  - Lista de Projetos do workspace ativo (com contagem de chunks ao lado).
  - `Sair` — encerra com segurança.
- **Notificações nativas**: `notify-send` (Linux GNOME/KDE), `osascript` (macOS), balão de bandeja via PowerShell (Windows 11).

### Gerenciar a bandeja pela CLI

```bash
ai-memory tray                  # abre a bandeja manualmente (precisa AiMemory.Tray instalado)
ai-memory tray install          # instala AiMemory.Tray + autostart
ai-memory tray status           # mostra installed/running/autostart path/executable path
ai-memory tray update           # recria o autostart apontando para o executável atual
ai-memory tray uninstall        # remove autostart e encerra o processo
ai-memory tray remove           # alias de uninstall
ai-memory tray setup            # assistente interativo
```

### Arquivos criados por plataforma

| Plataforma | Autostart | Outros |
|---|---|---|
| **Linux** | `~/.config/autostart/ai-memory-tray.desktop` | `~/.ai-memory/tray.pid` |
| **macOS** | `~/Library/LaunchAgents/com.aimemory.tray.plist` (logs em `~/Library/Logs/AiMemory`) | `launchctl bootstrap/kickstart` |
| **Windows** | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\ai-memory-tray.lnk` | atalho via WScript Shell |

> > A resolução do executável do tray segue: shim da global tool (`~/.dotnet/tools/ai-memory-tray`) → apphost publicado → `dotnet ai-memory.dll`. Isso evita o erro comum de autostart apontar para `dotnet run` ou path temporário interno.

---

## Configuração

### Arquivo principal: `~/.ai-memory/config.json`

Criado em modo `0600` (Unix; dir `0700`). Campos em **camelCase**:

```json
{
  "activeWorkspace": "claps",
  "workspaces": [
    { "name": "claps", "projects": [ { "name": "gestor", "path": "/path/to/gestor" } ] }
  ],
  "database": "ai_memory",
  "databaseHost": "localhost",
  "databasePort": 5432,
  "databaseUser": "postgres",
  "ollamaBaseUrl": "http://localhost:11434",
  "embeddingModel": "bge-m3",
  "semanticModel": "qwen2.5-coder:7b"
}
```

A senha **não** é persistida por padrão (preferir env var). Se `database` contiver `=` é tratada como connection string completa (host/port/user/password não são perguntados no setup).

### Padrões de extração: `~/.aimemory/patterns.json`

Permite customizar heurísticas de `rules` e `knowledge` sem recompilar:

```json
{
  "rules":    { "contentPatterns": ["bloquead", "cancelad", "vencid", "elegiv", "RuleFor", "AddFailure"] },
  "knowledge": { "filePathPatterns": ["%.csproj", "%Program.cs", "%Startup.cs"],
                 "contentPatterns": ["HttpClient", "MassTransit", "MediatR", "EntityFramework", "TODO", "FIXME"] }
}
```

Em ausência/erro, usa `PatternsConfig.Default` com as palavras-chave pt-BR já embutidas.

### Variáveis de ambiente (override em relação ao config)

| Variável | Sobrescreve |
|---|---|
| `AI_MEMORY_DB` | `database` (ou connection string completa se tiver `=`) |
| `AI_MEMORY_DB_HOST` | `databaseHost` |
| `AI_MEMORY_DB_PORT` | `databasePort` |
| `AI_MEMORY_DB_USER` | `databaseUser` |
| `AI_MEMORY_DB_PASSWORD` | `databasePassword` (preferida sobre persistir em `config.json`) |
| `AI_MEMORY_OLLAMA` | `ollamaBaseUrl` |
| `AI_MEMORY_EMBED_MODEL` | `embeddingModel` |
| `AI_MEMORY_SEMANTIC_MODEL` | `semanticModel` |
| `AI_MEMORY_PARALLELISM` | paralelismo padrão |

Se `AI_MEMORY_DB` for uma connection string completa, ela tem prioridade e o setup não pergunta host/porta/usuário/senha separadamente.

---

## Schema do banco (PostgreSQL + pgvector)

Migrações em `sql/`, aplicadas em ordem numérica e rastreadas em `ai_schema_migrations` (nome + SHA256):

- `000_schema.sql` — schema base + extensions `vector`, `pgcrypto`.
- `010_compat.sql` — migrações compatáveis (timestamps, CHECK constraints de status/stage, índices HNSW, etc.).
- `020_hybrid_search.sql` — `tsvector` + triggers + GIN para **busca híbrida**.

### Tabelas

| Tabela | Para que serve |
|---|---|
| `ai_workspaces` | Grupos de projetos (recorte de trabalho/cliente/produto). |
| `ai_projects` | Projetos indexados (id `integer`, identificado por `root_path` único). |
| `ai_workspace_projects` | Vínculo muitos-para-muitos workspace↔projeto (`ON DELETE CASCADE`). |
| `ai_chunks` | Pedaços semânticos dos arquivos (classe, método, procedure, seção, config). Guarda `content`, `content_hash`, `embedding VECTOR(1024)`, `file_path`, `symbol_name`, `chunk_type`, `language`, e `search_vector` (tsvector). Único por `(project_id, file_path, content_hash)`. |
| `ai_business_rules` | Regras de negócio extraídas. `status` ∈ `candidate`/`accepted`/`rejected`. Guarda `evidence`, `symbol_name`, `chunk_id` (FK), `confidence NUMERIC(5,2)`, `embedding`. |
| `ai_knowledge` | Conhecimento de engenharia. Mesma forma de `ai_business_rules`, com `kind` e `content`. |
| `ai_extraction_chunk_state` | Estado incremental por `(chunk_id, stage)`. `stage` ∈ `rules`/`knowledge`, `status` ∈ `processed`/`failed`, com `content_hash` e `error` (última falha). |
| `ai_symbols` | Símbolos Roslyn (`kind` ∈ class/interface/struct/enum/record, `full_name`, `file_path`, linhas). Único por `(project_id, full_name)`. |
| `ai_symbol_relations` | Arestas do grafo: `relation` ∈ `calls`/`inherits`/`implements`/`uses`/`references`. PK `(source_id, target_id, relation)`. |

### Regras de estado (importante)

- Regras/conhecimento sem **evidência** copiada do chunk devem ser tratados como **hipótese**, não fato consolidado.
- `candidate` é descoberta ainda não revisada; `accepted` é validada; `rejected` é falso positivo ou obsoleta.
- A tool **nunca** promove automaticamente `candidate → accepted`. Revisão humana é explícita.

---

## Estratégia de chunking

A qualidade do sistema depende mais do chunking do que do banco vetorial. **Regra principal: dividir por significado, não por número fixo de caracteres.**

| Linguagem | Estratégia |
|---|---|
| **C#** | **Roslyn** — classe pequena vira 1 chunk `type`; classe grande vira vários chunks por membro (método/propriedade) com prefixo de `usings`/`namespace`/declaração de tipo. Fallback textual se não houver tipos reconhecíveis. `symbol_name` = `Namespace.Class.Method(params)`. |
| **SQL** | Divide por `GO`, identifica `create/alter procedure|view|function|trigger`. |
| **Markdown** | Divide por `#`/`##`/`### headings`. |
| **JSON/YAML/config** | Divide por objeto principal ou arquivo inteiro se pequeno. |
| **.razor/.cshtml** | Chunking nativo (não-genérico). |

**Tamanho máximo sugerido:** ~6.000 caracteres por chunk (`MaxChunkLength`). Antes do embedding, cada chunk recebe um **prefixo de contexto** (`[CONTEXT: Project: X | File: Y | Lang: C# | Symbol: Z]`) via `ContextualChunkingService` — esse prefixo não é gravado no `content` do banco (economiza tokens nas respostas) mas realimenta o embedding para melhorar a namespaces.

Na resposta do MCP, o conteúdo sofre **compressão de contexto** (Netflix-Headroom-style): headers de licença viram `[license header omitted]`, blocos grandes de `using` viram `[usings omitted]`, XML doc tags são truncadas, e métodos não-alvo têm o corpo colapsado em `/* body omitted */`.

---

## State e serviços (resumo técnico)

### Serviços principais (`Services/`)

| Serviço | O que faz |
|---|---|
| `OllamaService` | Embeddings (`api/embeddings`/`api/embed`) e geração JSON (`api/generate`). Retry **Polly** (3 tentativas, backoff exponencial + jitter). Batch embedding com fallback por chunk. |
| `PgVectorService` | Fachada sobre 6 repositórios. `UpsertChunkAsync`, `SearchAsync`, `FindRelatedFilesAsync`, `GetChunksForRuleExtractionAsync`, `UpsertBusinessRuleCandidateAsync`, símbolos, etc. |
| `ChunkRepository` / `RuleRepository` / `KnowledgeRepository` / `SymbolGraphRepository` / `ExtractionStateRepository` / `SearchService` | Um repositório por agregado. RRF híbrido via CTEs. Orphan cleanup. Upsert idempotente. |
| `ChunkingService` | Enumeração de arquivos (prefere `git ls-files`), chunking por linguagem, detecção de migrations EF e testes. |
| `ContextualChunkingService` | Prefixo `[CONTEXT: ...]` para embeddings. |
| `ContextCompressionService` | Compressão de código/evidência nas respostas MCP. |
| `RuleExtractionService` / `KnowledgeExtractionService` | Extração heurística (regex + palavras-chave) e semântica (prompt JSON) com validação de evidência. |
| `TextNormalizationService` | Normalização de frases, regex de exceções/validações/ErrosContext, tabela de KnowledgePatterns, detectores pt-BR. |
| `RerankerService` | Reranking heurístico pós-busca (boost por símbolo, penalty para config/docs em queries estruturais). |
| `SymbolGraphService` | Parser Roslyn para grafo de símbolos (classes, interfaces, herança, chamadas). |
| `SqlPredicates` | Biblioteca de predicados SQL (candidatos, exclusões EF/teste, gate semântico). |
| `TraySetupService` | Instalação/remoção/status do autostart por plataforma; resolve shim do tray. |
| `ConfigService` (em `Configuration/`) | Carrega/salva `~/.ai-memory/config.json` (0600), normaliza workspaces/projetos, resolve overrides env. |
| `HashService` | SHA-256 (usado para `content_hash` incremental). |

### Cache de embeddings (MCP)

O servidor MCP mantém um `MemoryCache` (LRU, **1000 entradas**, TTL **1 hora**) para evitar re-embedar a mesma query/Symbol repetidamente dentro de uma sessão.

---

## Testes

```bash
dotnet test ai-memory.slnx
# ou apenas os testes de unidade (sem Docker):
dotnet test tests/AiMemory.Tests/AiMemory.Tests.csproj --filter "FullyQualifiedName~Unit"
```

- **Framework:** xUnit 2.9.2 + FluentAssertions 6.12.1.
- **Integração:** Testcontainers.PostgreSql com a imagem `pgvector/pgvector:pg17` (Docker necessário).
- **Cobertura atual:** 36 unit `[Fact]` + 5 integration = **41 testes**.
- Unit cobrem: `TextNormalizationService`, `ChunkingService` (C#/SQL/Markdown/fallback/EF/teste), `ContextCompressionService`, `ContextualChunkingService`, `HashService`, `RerankerService`.
- Integração cobre: upsert+search com embeddings, stats de `rules`, orphan cleanup, idempotência das migrações.

---

## CI/CD (GitHub Actions)

- **`.github/workflows/ci.yml`** — em `pull_request` e `push` para `main`: matrix **ubuntu/macos/windows**, `dotnet restore/build/test --logger trx` e publicação de relatório via `dorny/test-reporter`.
- **`.github/workflows/release.yml`** — em tags `v*`: extrai a versão da tag, `dotnet pack -p:Version=...`, `dotnet nuget push` (NuGet.org, precisa de `NUGET_API_KEY` em Secrets) e GitHub Release com `body_path: CHANGELOG.md`.

---

## Instruções para agentes (prompts/skills)

Os prompts e skills ficam em `ai-config-files/` para reaproveitar em outros projetos:

- [ai-memory MCP first](ai-config-files/ai-memory-mcp-first.md) — prompt de sistema forçando o agente a **consultar o `ai-memory` antes de responder** sobre código/regras/refatoração/bugs. Define tags `[Certain]`/`[Likely]`/`[Guessing]` e expressões proibidas ("Great question", "You're absolutely right"...).
- [Skill: context-first response](ai-config-files/skills/ma9-context-first-response.md) — fluxo de 5 passos: ai-memory → código → regras → arquitetura → resposta.
- [Skill: refactor impact-first](ai-config-files/skills/ma9-refactor-impact-first.md) — propostas de refator ancoradas no código existente.

Copie esses arquivos (ou os desejados) para os projetos em que o agente deve consultar o `ai-memory`, e cadastre como instrução de projeto, prompt compartilhado ou skill no Rider/VS Code/Codex/opencode/etc.

---

## O que esperar / limitações

- **Não treina o modelo.** O `ai-memory` cria e mantém uma **memória pesquisável** (embeddings + indexação + extração); o raciocínio continua sendo do LLM externo (Ollama ou da IDE).
- **Foco .NET.** Roslyn é explorado para C#; outras linguagens usam chunking textual/regex. Suporte a TypeScript/Python/Java está fora do escopo desta fase.
- **Latência de embedding** (~60–230ms por query no Ollama local) é aceitável; otimizações agressivas (ONNX embutido, HNSW tunado, cache persistido em SQLite) foram **propositadamente adiadas** (ver ADR-003 em `planning/06-technical/`).
- **STDIO-only** para MCP. Não há transporte HTTP nesta fase (também fora do escopo por ADR).
- **Revisão humana obrigatória** para `accepted`/`rejected` em regras e conhecimento. O sistema não decide sozinho o que é fato.
- **Dependências externas**: PostgreSQL 14+ com pgvector, Ollama com `bge-m3` (1024 dims). Se algum mudar formato/dimensão, será breaking change de schema.

---

## Roadmap

Resumo de marcos (detalhes em `planning/`):

| Versão | Foco | Status |
|---|---|---|
| **v0.2.0** | Estabilização: build, FK cascade, migrations determinísticas, MaxChunkLength unificado, MCP stdout guardado, testes, split de `PgVectorService`, batch embedding, paralelismo, Polly, watcher real, cache LRU, heurísticas configuráveis, CI/CD | **current** |
| **v0.3.0** | Performance: batch `/api/embed` com fallback, paralelismo de `rules`/`knowledge`, retry Polly v8 | planejada |
| **v0.4.0** | MCP `2025-11-25` (Tool Annotations, Structured Output, ícones, Tool Execution Errors, `title`) + **split do tray** em pacote próprio | em curso (tray já separado como `AiMemory.Tray`) |
| **v0.5.0** | Maturidade: CI/CD, watcher real, `patterns.json`, cache LRU+TTL | partes já entregues |
| **v0.6.0+** | MCP `2026-07-28` (stateless, `server/discover`, `subscriptions/listen`) — aguardando stable + ≥2 clients | futuro |

Próximas melhorias planejadas (backlog): watcher com debounce robusto, evolução do chunking C# com símbolos/relações/chamadas, extração semântica com deduplicação vetorial e agrupamento de evidências, tabela de relações entre símbolos, reranking, dashboard com ações de manutenção, comandos de limpeza/reindexação por projeto.

---

## Observações finais

Este projeto não treina o modelo. Ele cria uma **memória pesquisável**:

```text
modelo LLM = raciocina
bge-m3     = transforma texto em vetor
pgvector   = encontra contexto parecido
Roslyn     = entende a estrutura do C#
ai-memory  = mantém tudo atualizado
MCP        = conecta essa memória ao agente
```

Tudo **local**, auditável e sob seu controle — ideal para times .NET que usam IA nas IDEs sem querer vazar código-fonte nem queimar orçamento de tokens.