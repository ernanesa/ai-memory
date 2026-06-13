# AI Memory Tool

Plataforma local de memória para agentes de IA usando **Ollama + bge-m3 + PostgreSQL + pgvector + .NET Tool + MCP**.

> Este projeto foi criado para ser aberto no Rider ou VS Code. O arquivo `.csproj` já está pronto para ser tratado como uma aplicação console empacotável como `dotnet tool`.

---

## Objetivo

Criar uma memória local para agentes capaz de:

- indexar múltiplos projetos;
- armazenar trechos de código, documentação e regras de negócio;
- consultar contexto relevante antes de responder;
- reduzir tokens enviados ao LLM;
- integrar Rider, VS Code e Codex por MCP.

Arquitetura:

```text
Rider / VS Code / Codex
        │
        ▼
 ai-memory mcp
        │
 ┌──────┴─────────┐
 ▼                ▼
Ollama         PostgreSQL
bge-m3          pgvector
        ▲
        │
 ai-memory index/watch
        ▲
        │
  Projetos locais
```

---

## 1. Instalação da tool

Na raiz do projeto:

```bash
dotnet pack -c Release
dotnet tool install --global --add-source ./bin/Release AiMemory.Tool
```

Atualizar após alterações:

```bash
dotnet pack -c Release
dotnet tool update --global --add-source ./bin/Release AiMemory.Tool
```

## 2. Setup

Depois de instalar a tool:

```bash
ai-memory setup
```

O setup funciona em duas fases:

1. Coleta todas as respostas do usuário: banco, usuário/senha opcionais do PostgreSQL, URL do Ollama, modelo de embedding, workspaces, projetos e ações automáticas permitidas.
2. Executa o plano até o fim sem novas perguntas.

Na etapa de workspaces, informe um nome como `claps` ou `pagueOn`. Para cada workspace, informe um diretório de projeto por vez. O nome do projeto é inferido automaticamente pelo nome da pasta. Quando não quiser adicionar mais projetos naquele workspace, pressione Enter sem preencher o diretório. Depois o setup pergunta se deseja configurar outro workspace.

Durante a execução, a interface usa cores para destacar estados, perguntas, avisos e sucesso. Logs de instalação e comandos externos são compactados: o setup exibe apenas as últimas linhas relevantes em cor discreta, mantendo a interação principal legível. Ao final, ele mostra um resumo colorido com o que foi concluído, ignorado ou ficou pendente.

Quando possível, ele também automatiza a preparação local:

- instala dependências faltantes via Homebrew: PostgreSQL, pgvector e Ollama;
- inicia PostgreSQL e Ollama via `brew services`;
- cria o banco `ai_memory` usando o usuário PostgreSQL informado;
- aplica o schema SQL;
- lista modelos disponíveis no Ollama;
- baixa o modelo escolhido, por padrão `bge-m3`.

O usuário padrão do PostgreSQL é o usuário atual do sistema, que é o comportamento comum de uma instalação via Homebrew. Se você usa outro usuário, por exemplo `postgres`, informe esse valor no setup. A senha é opcional: deixe vazia para usar autenticação local, `trust`, peer ou `.pgpass`.

Se Homebrew não estiver instalado ou algum serviço não puder ser iniciado automaticamente, o setup mostra o que faltou e pode ser executado novamente depois do ajuste.

---

## 3. Para que servem as tabelas

### `ai_workspaces`

Guarda grupos de projetos.

Um workspace representa um recorte de trabalho, cliente, produto ou contexto que pode conter vários projetos.

### `ai_projects`

Guarda os projetos indexados.

O projeto é identificado principalmente pelo `root_path`, permitindo que o mesmo projeto participe de mais de um workspace.

### `ai_workspace_projects`

Guarda o vínculo entre workspaces e projetos.

Permite a relação muitos-para-muitos:

- um workspace pode ter vários projetos;
- um projeto pode aparecer em vários workspaces.

Isso evita misturar os 12+ projetos em uma única massa sem contexto, sem duplicar o mesmo projeto quando ele for reutilizado por mais de um workspace.

### `ai_chunks`

Guarda pedaços semânticos dos arquivos:

- classe;
- método;
- procedure SQL;
- seção markdown;
- arquivo de configuração.

Cada chunk possui:

- conteúdo original;
- hash;
- embedding;
- arquivo de origem;
- projeto;
- linguagem;
- símbolo.

### `ai_business_rules`

Guarda regras de negócio extraídas do código ou documentação.

Exemplo:

```text
Cliente bloqueado não pode gerar nova cobrança.
```

### `ai_knowledge`

Guarda conhecimento de engenharia:

- decisões arquiteturais;
- padrões do time;
- convenções;
- integrações;
- riscos;
- observações geradas pelos agentes.

---

## 4. Estratégia de chunking recomendada

A qualidade do sistema depende mais do chunking do que do banco vetorial.

### Regra principal

Não dividir por número fixo de caracteres como primeira escolha.

Dividir por significado.

### C#

Preferência:

```text
classe pequena     -> 1 chunk
método relevante   -> 1 chunk
classe grande      -> vários chunks por método/propriedade
interface/record   -> 1 chunk
```

Metadados desejados:

```text
language = csharp
chunk_type = type | member | file
symbol_name = NomeDaClasseOuMetodo
```

### SQL

Dividir por:

```text
procedure
view
function
trigger
bloco separado por GO
```

### Markdown

Dividir por:

```text
# título
## seção
### subseção
```

### JSON/YAML/config

Dividir por objeto principal ou arquivo inteiro quando pequeno.

### Arquivos ignorados

```text
.git/
bin/
obj/
node_modules/
dist/
coverage/
packages/
.idea/
.vs/
```

### Tamanho máximo sugerido

Começar com chunks de até aproximadamente 6.000 caracteres.

Depois evoluir para:

- Roslyn para C#;
- parser SQL;
- extração de símbolos;
- relacionamento entre classes, interfaces e chamadas.

---

## 5. Uso

```bash
ai-memory setup
ai-memory workspace list
ai-memory workspace add claps
ai-memory workspace use claps
ai-memory project add
ai-memory project add --workspace pagueOn
ai-memory project list --workspace claps
ai-memory index
ai-memory index --workspace claps
ai-memory index gestor --workspace claps
ai-memory search "onde valida limite de crédito?"
ai-memory watch
ai-memory dashboard
ai-memory dashboard serve
ai-memory mcp
```

O dashboard possui dois modos:

```bash
ai-memory dashboard
ai-memory dashboard --workspace claps
ai-memory dashboard --project clapsapi
ai-memory dashboard serve
ai-memory dashboard serve --port 5050
```

O comando `dashboard` mostra um resumo no terminal. O comando `dashboard serve` inicia uma interface web local, por padrão em `http://localhost:5050`.

O comando `ai-memory mcp` inicia o servidor MCP via STDIO. Ele já expõe ferramentas para agentes consultarem a memória local indexada:

- `search_code`: busca semântica em código, documentação e arquivos de configuração;
- `search_business_rules`: busca semântica em regras de negócio extraídas;
- `find_related_files`: encontra arquivos relacionados a uma consulta ou a outro arquivo indexado.

Variáveis opcionais:

```bash
export AI_MEMORY_DB="Host=localhost;Database=ai_memory;Username=postgres"
export AI_MEMORY_DB_USER="postgres"
export AI_MEMORY_DB_PASSWORD="senha"
export AI_MEMORY_OLLAMA="http://localhost:11434"
export AI_MEMORY_EMBED_MODEL="bge-m3"
```

---

## 6. Configuração MCP no Rider

Em **Settings > Tools > AI Assistant > Model Context Protocol (MCP)**, adicionar servidor STDIO:

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

---

## 7. Configuração MCP no VS Code

Configuração genérica:

```json
{
  "servers": {
    "ai-memory": {
      "command": "ai-memory",
      "args": ["mcp"]
    }
  }
}
```

A localização exata do arquivo depende da extensão/agente usado.

---

## 8. Skills / instruções para agentes

### Skill: engenharia com memória

```text
Antes de responder sobre código, arquitetura ou regra de negócio:

1. Consulte a ferramenta ai-memory.
2. Busque código relacionado.
3. Busque regras de negócio relacionadas.
4. Busque decisões arquiteturais relacionadas.
5. Só então responda.

Ao responder:

- cite arquivos relevantes;
- diferencie fato de inferência;
- destaque incertezas;
- evite sugerir padrões que conflitem com o código existente.
```

### Skill: refatoração

```text
Antes de propor refatoração:

1. Procure implementações similares.
2. Procure interfaces já existentes.
3. Procure padrões usados no mesmo projeto.
4. Procure regras de negócio afetadas.
5. Evite duplicação.

Ao propor mudança:

- indique impacto;
- indique arquivos envolvidos;
- indique riscos;
- proponha passos pequenos.
```

---

## 9. Prompt completo para análise do projeto inteiro

Use depois que o indexador tiver processado os projetos desejados.

Este prompt foi feito para criar memória persistente no `ai_memory`, não para gerar relatórios Markdown.

```text
Você é um arquiteto de software especializado em sistemas .NET corporativos.

Analise todo o workspace usando a memória local do ai-memory.

Infraestrutura obrigatória:

- PostgreSQL/pgvector é a fonte persistente da análise.
- Ollama deve ser usado para gerar embeddings.
- O modelo de embedding deve ser o configurado no ambiente ou na tool, normalmente `bge-m3`.
- Use `AI_MEMORY_DB`, `AI_MEMORY_OLLAMA` e `AI_MEMORY_EMBED_MODEL` quando estiverem disponíveis.
- Consulte primeiro as ferramentas MCP `search_code`, `search_business_rules` e `find_related_files`.
- Quando houver acesso de escrita ao banco, salve as descobertas diretamente nas tabelas `ai_business_rules` e `ai_knowledge`.

Proibição importante:

- Não crie arquivos `.md`, relatórios Markdown, mapas Markdown ou documentos locais como resultado da análise.
- Não use arquivos Markdown como substituto para persistência.
- Se não houver ferramenta MCP de escrita nem acesso direto ao PostgreSQL, pare e informe que falta permissão/capacidade de escrita na memória. Não gere um `.md` alternativo.

Objetivos da análise:

1. Identificar arquitetura geral dos sistemas.
2. Mapear bounded contexts.
3. Identificar regras de negócio explícitas e implícitas.
4. Identificar integrações externas.
5. Identificar entidades principais.
6. Identificar fluxos críticos.
7. Identificar dependências entre projetos.
8. Identificar duplicações e inconsistências.
9. Identificar riscos técnicos.
10. Construir uma base de conhecimento reutilizável dentro do banco `ai_memory`.

Fluxo obrigatório:

1. Confirmar que o workspace/projeto já foi indexado.
2. Consultar a memória existente antes de inferir qualquer conclusão.
3. Para cada área analisada, buscar código relacionado, regras existentes e arquivos relacionados.
4. Extrair descobertas pequenas, objetivas e reutilizáveis.
5. Classificar cada descoberta como regra de negócio, decisão arquitetural, integração, entidade, fluxo crítico, risco técnico, padrão, inconsistência ou oportunidade de refatoração.
6. Gerar embedding do texto final da descoberta usando Ollama e o modelo configurado.
7. Persistir cada descoberta no PostgreSQL com referência aos arquivos de origem.
8. Depois de salvar, validar por consulta semântica que os registros ficaram recuperáveis.

Para regras de negócio, grave em `ai_business_rules`:

- `project_id`: projeto responsável, quando identificável.
- `title`: nome curto da regra.
- `description`: descrição objetiva da regra.
- `source_file`: arquivo principal de origem.
- `confidence`: número de 0.00 a 1.00.
- `embedding`: embedding gerado via Ollama.

Para demais descobertas, grave em `ai_knowledge`:

- `project_id`: projeto responsável, quando identificável.
- `kind`: `architecture`, `bounded_context`, `integration`, `entity`, `critical_flow`, `technical_risk`, `pattern`, `inconsistency` ou `refactoring_opportunity`.
- `title`: nome curto da descoberta.
- `content`: explicação objetiva, com arquivos e dependências relevantes.
- `source`: arquivo, consulta ou conjunto de arquivos que sustentam a descoberta.
- `confidence`: número de 0.00 a 1.00.
- `embedding`: embedding gerado via Ollama.

Critérios de qualidade:

- Diferencie fato observado de inferência.
- Use confiança alta apenas quando houver evidência direta em código ou documentação indexada.
- Evite descobertas grandes demais; prefira registros pequenos e fáceis de recuperar por busca semântica.
- Não duplique conhecimento já existente; atualize ou complemente quando possível.
- Sempre associe a descoberta ao projeto e aos arquivos relevantes.
- Não sugira padrões que conflitem com o código existente.

Resposta final ao usuário:

- Informe quantos registros foram salvos em `ai_business_rules`.
- Informe quantos registros foram salvos em `ai_knowledge`.
- Liste apenas um resumo curto por categoria.
- Informe incertezas ou partes não analisadas.
- Não entregue relatório Markdown completo; a fonte de verdade deve ser o banco.
```

---

## 10. Roadmap do projeto

### MVP atual

- schema PostgreSQL;
- indexação básica;
- busca vetorial básica;
- chunking inicial;
- comandos `index`, `search`, `watch` e `mcp`;
- servidor MCP STDIO funcional;
- ferramentas MCP `search_code`, `search_business_rules` e `find_related_files`.

### Próximas melhorias

1. Implementar watcher real com debounce.
2. Trocar chunking C# por Roslyn.
3. Criar extração automática de regras de negócio.
4. Criar tabela de relações entre símbolos.
5. Criar reranking.
6. Evoluir dashboard de memória com ações de manutenção.
7. Criar comandos para limpeza e reindexação por projeto.

---

## 11. Observações importantes

Este projeto não treina o modelo.

Ele cria uma memória pesquisável:

```text
modelo LLM = raciocina
bge-m3 = transforma texto em vetor
pgvector = encontra contexto parecido
ai-memory = mantém tudo atualizado
MCP = conecta essa memória ao agente
```
