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

Durante a execução, a interface usa cores para destacar estados, perguntas, avisos e sucesso. Logs de instalação e comandos externos são compactados: o setup exibe apenas as últimas linhas relevantes em cor discreta, mantendo a interação principal legível. Downloads de modelos via `ollama pull` são exceção: a saída é exibida em tempo real para acompanhar o progresso. Ao final, ele mostra um resumo colorido com o que foi concluído, ignorado ou ficou pendente.

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

Para aumentar confiabilidade, regras podem ser registradas como:

- `candidate`: descoberta ainda não revisada;
- `accepted`: regra validada;
- `rejected`: falso positivo ou regra obsoleta.

Cada regra pode guardar evidência (`evidence`), símbolo de origem (`symbol_name`) e vínculo com o chunk (`chunk_id`). Regra sem evidência deve ser tratada como hipótese, não como fato consolidado.

### `ai_knowledge`

Guarda conhecimento de engenharia:

- decisões arquiteturais;
- padrões do time;
- convenções;
- integrações;
- riscos;
- observações geradas pelos agentes.

Assim como regras de negócio, registros de conhecimento podem ser:

- `candidate`: descoberta ainda não revisada;
- `accepted`: conhecimento validado;
- `rejected`: falso positivo ou informação obsoleta.

Cada registro pode guardar evidência (`evidence`), símbolo de origem (`symbol_name`) e vínculo com o chunk (`chunk_id`). Conhecimento sem evidência deve ser tratado como inferência, não como fato consolidado.

### `ai_extraction_chunk_state`

Controla o processamento incremental das fases de extração.

Guarda, por chunk e por fase (`rules` ou `knowledge`):

- `content_hash`: versão do conteúdo processado;
- `status`: `processed` ou `failed`;
- `processed_at`: quando a fase processou o chunk;
- `error`: erro da última tentativa, quando houver.

Isso permite que `ai-memory index rules --candidate-limit 2000` processe lotes diferentes a cada execução. Chunks já processados são ignorados enquanto o `content_hash` não mudar. Se um arquivo for reindexado ou atualizado pelo `watch`, o hash do chunk muda e ele volta a ser candidato para `rules` e `knowledge`.

---

## 4. Estratégia de chunking recomendada

A qualidade do sistema depende mais do chunking do que do banco vetorial.

### Regra principal

Não dividir por número fixo de caracteres como primeira escolha.

Dividir por significado.

### C#

Implementação atual: parser Roslyn para arquivos `.cs`, com fallback textual simples quando não houver tipos reconhecíveis.

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

Quando o projeto está dentro de um repositório Git, a enumeração de arquivos usa `git ls-files --cached --others --exclude-standard`, respeitando `.gitignore`, `.git/info/exclude` e excludes globais do Git. Fora de um repositório Git, ou se o comando `git` não estiver disponível, a tool usa os ignores fixos abaixo:

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
.vscode/
```

Além dos diretórios acima, arquivos C# de migrations geradas pelo Entity Framework são ignorados na indexação de chunks e nas fases `rules`/`knowledge`. A detecção não depende apenas do caminho: a tool procura estrutura/conteúdo típico de EF, como classes que herdam `Migration` ou `ModelSnapshot`, atributos `[Migration]`/`[DbContext]`, `MigrationBuilder` e `BuildTargetModel`. Ao rodar `index chunks`, chunks antigos de arquivos identificados como migrations EF também são removidos do escopo do workspace/projeto indexado.

Projetos e arquivos de teste também são ignorados por padrão em `chunks`, `rules` e `knowledge`. A detecção considera projetos com `Microsoft.NET.Test.Sdk`, `xunit`, `NUnit`, `MSTest`, `<IsTestProject>true</IsTestProject>` ou `coverlet.collector`, além de arquivos C# com atributos como `[Fact]`, `[Theory]`, `[Test]`, `[TestMethod]`, `[TestClass]`, `[TestFixture]`, `[SetUp]` e nomes/pastas comuns como `Tests`, `UnitTests`, `IntegrationTests` e `Specs`. Ao rodar `index chunks`, chunks antigos identificados como teste também são removidos do escopo do workspace/projeto indexado.

### Tamanho máximo sugerido

Começar com chunks de até aproximadamente 6.000 caracteres.

Depois evoluir para:

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
ai-memory index --semantic
ai-memory index --workspace claps
ai-memory index chunks --project gestor --workspace claps
ai-memory index rules knowledge --workspace claps
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

O comando `index` é organizado por fases:

```bash
ai-memory index
ai-memory index chunks
ai-memory index rules
ai-memory index knowledge
ai-memory index chunks rules
ai-memory index rules knowledge
ai-memory index chunks knowledge
ai-memory index chunks rules knowledge
ai-memory index chunks --project gestor --workspace claps
ai-memory index rules knowledge --candidate-limit 10000
ai-memory index rules knowledge --semantic --semantic-model qwen2.5-coder:7b --candidate-limit 500
ai-memory index rules knowledge --semantic --refresh --candidate-limit 500
```

Sem fases explícitas, `ai-memory index` representa o pipeline completo:

```text
chunks -> rules -> knowledge
```

As fases são:

- `chunks`: lê os arquivos dos projetos configurados, quebra em chunks, gera embeddings e grava em `ai_chunks`;
- `rules`: usa chunks já indexados para extrair/reconciliar regras de negócio em `ai_business_rules`;
- `knowledge`: usa chunks já indexados para extrair/reconciliar conhecimento técnico em `ai_knowledge`.

O filtro de projeto deve ser informado com `--project`. A sintaxe antiga `ai-memory index gestor --workspace claps` é aceita temporariamente por compatibilidade, mas deve ser substituída por `ai-memory index --project gestor --workspace claps`.

As fases `rules` e `knowledge` fazem extração heurística conservadora a partir dos chunks já indexados. Elas criam novos candidatos, buscam evidências para candidatos existentes, atualizam confiança e mantêm `accepted`/`rejected` como estados explícitos de revisão. Candidatos nunca são promovidos automaticamente para `accepted`.

Essas fases são incrementais por `content_hash`. A tool processa chunks candidatos que nunca foram processados naquela fase, que falharam anteriormente ou cujo conteúdo mudou desde a última extração.

A extração de `rules` e `knowledge` ignora chunks que pareçam migrations geradas pelo Entity Framework ou código de teste, mesmo que esses chunks já existam no banco de uma indexação anterior. Isso evita gastar embedding/modelo semântico com `Migration`, `*.Designer.cs`, `*ModelSnapshot.cs`, asserts, fixtures, mocks e cenários artificiais que normalmente não são a melhor fonte de regras de negócio.

A fase `rules` considera sinais explícitos de regra de negócio, incluindo exceções de domínio, validações, FluentValidation e padrões de erro/notificação como `ErroContext`, `ErrosContext`, `AdicionarErro`, `AddErro`, `AddFailure`, `AddNotification`, `Notificar`, `RuleFor`, `Validator`, `TemErro`, `HasError`, `IsValid` e termos de domínio como bloqueado, cancelado, vencido, elegível e permitido.

Com `--semantic`, as fases `rules` e `knowledge` usam extração semântica nos chunks candidatos, mas continuam respeitando o estado incremental: chunks já processados com o mesmo `content_hash` são ignorados. A tool chama o modelo informado por `--semantic-model` ou `AI_MEMORY_SEMANTIC_MODEL`, pede JSON estruturado, exige evidência copiada do próprio chunk e descarta itens sem evidência. Esse modo é mais lento, mas ajuda na descoberta inicial de projetos com padrões de validação, erro ou arquitetura que a heurística ainda não conhece.

Para `rules --semantic`, a seleção passa por um gate antes de chamar o modelo: handlers, services, application/domain services, use cases, policies, specifications e queries com sinais de decisão/regra são priorizados; migrations, mappings EF, DTOs simples, interfaces puras, constants/configurations/options e fatos técnicos evidentes são evitados. Depois da resposta do modelo, a tool rejeita candidatos que pareçam apenas constante, GUID, assinatura de método, capacidade de consulta ou descrição técnica, como `permite obter`, `busca`, `retorna` ou `cria lista`, quando não houver restrição/decisão de domínio.

Use `--refresh` quando quiser revisitar todos os chunks que combinam com o escopo da fase, mesmo que já estejam marcados como processados. Isso é útil quando a estratégia de extração mudou e você quer refazer a análise, por exemplo:

```bash
ai-memory index rules knowledge --semantic --refresh --candidate-limit 500
```

Durante `chunks`, `rules` e `knowledge`, a tool mostra um painel de progresso com 4 linhas em terminais interativos, atualizado a cada 1 segundo sem ficar gerando novas linhas continuamente. O painel mostra fase, progresso, tempo decorrido, ETA, contadores, arquivo/símbolo atual e taxa média em formato tabular. Quando a saída é redirecionada para arquivo, pipe ou CI, a tool volta para logs lineares para evitar códigos de cursor no arquivo. O ETA é exibido como aproximação arredondada e suavizada, por exemplo `eta ~11h45m`, porque chamadas de LLM podem variar muito entre chunks e uma precisão em segundos tende a oscilar sem representar melhor o tempo real. Essas etapas podem demorar porque cada chunk ou candidato precisa gerar embedding antes de ser salvo.

Por padrão, `rules` e `knowledge` analisam todos os chunks candidatos encontrados no escopo. Para limitar o recorte:

```bash
ai-memory index rules --candidate-limit 5000
ai-memory index knowledge --candidate-limit 10000
ai-memory index rules knowledge --candidate-limit 2000
ai-memory index rules knowledge --semantic --candidate-limit 500
ai-memory index rules knowledge --semantic --refresh --candidate-limit 500
```

A saída inicial de `rules` e `knowledge` mostra uma tabela de escopo com total de chunks, candidatos encontrados, já processados, novos pendentes, falhos, alterados por hash, acionáveis, selecionados, `refresh` e `candidate limit`. Quando nenhum `--candidate-limit` é informado, a tool mostra um aviso em amarelo informando que todos os chunks candidatos selecionados serão processados, que a etapa pode demorar porque cada candidato gera embedding, e que é possível usar `--candidate-limit <n>` para processar um lote menor.

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
export AI_MEMORY_SEMANTIC_MODEL="qwen2.5-coder:7b"
```

Durante o `setup`, a tool pode puxar os modelos Ollama ausentes usados pelo fluxo padrão: `bge-m3` para embeddings e `qwen2.5-coder:7b` para extração semântica. O progresso do `ollama pull` é exibido em tempo real.

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

```md
---
name: ma9-context-first-response
description: 'Forca resposta baseada em contexto antes de opinar sobre codigo, arquitetura ou regra de negocio. Use quando pedir analise tecnica, explicacao de codigo, proposta de implementacao, revisao arquitetural ou recomendacao de padrao. Executa fluxo: consultar memoria, buscar codigo relacionado, buscar regras de negocio, buscar decisoes arquiteturais, e so depois responder com fatos vs inferencias, incertezas e referencias de arquivos.'
argument-hint: 'Tema/pergunta tecnica que precisa de resposta com lastro em codigo e contexto'
user-invocable: true
---

# Context First Response

## Objetivo
Gerar respostas tecnicas com rastreabilidade, reduzindo alucinacao e sugestoes que conflitem com o codigo existente.

## Quando usar
- Perguntas sobre codigo, arquitetura, regra de negocio ou trade-offs tecnicos.
- Pedidos de implementacao, refatoracao, code review ou troubleshooting.
- Situacoes em que o usuario quer evidencias concretas no repositorio.

## Nao usar
- Conversa casual sem conteudo tecnico.
- Solicitacoes sem necessidade de validacao por codigo (ex.: texto institucional).

## Fluxo obrigatorio
1. Consultar ai-memory antes de qualquer conclusao.
2. Buscar codigo relacionado ao pedido (arquivos, simbolos, chamadas e testes).
3. Buscar regras de negocio relacionadas (docs, contratos, validacoes, regras em codigo).
4. Buscar decisoes arquiteturais relacionadas (ADRs, convencoes, camadas, dependencias).
5. So entao montar a resposta final.

## Regras de decisao
- Se faltar evidencias em um dos 4 blocos (memoria, codigo, negocio, arquitetura), declarar explicitamente a lacuna.
- Se houver conflito entre sugestao e implementacao atual, priorizar aderencia ao codigo existente e sinalizar alternativa como opcional.
- Se nao houver certeza suficiente, perguntar apenas o minimo necessario para desbloquear.

## Criterios de qualidade antes de responder
- Ha pelo menos uma evidencia concreta de codigo relacionada ao tema.
- Ha indicacao clara do que e fato observado e do que e inferencia.
- Ha secao de incertezas/riscos quando aplicavel.
- Nao ha recomendacao que contradiga padrao vigente sem justificativa explicita.

## Formato de resposta
- Arquivos relevantes: liste os arquivos usados como base.
- Fatos observados: afirmacoes verificadas no contexto.
- Inferencias: hipoteses ou extrapolacoes, rotuladas como tal.
- Incertezas: pontos que dependem de confirmacao.
- Recomendacao aderente: proposta que respeita o codigo existente.

## Checklist rapido
- ai-memory consultada
- codigo relacionado consultado
- regra de negocio consultada
- decisoes arquiteturais consultadas
- fatos vs inferencias separados
- incertezas destacadas
- sem conflito com padrao existente
```

### Skill: refatoração

```md
---
name: ma9-refactor-impact-first
description: 'Guia propostas de refatoracao com base no codigo existente. Use quando pedir refatoracao, melhoria estrutural, simplificacao ou limpeza tecnica. Executa fluxo: buscar implementacoes similares, interfaces existentes, padroes do projeto e regras de negocio afetadas; evitar duplicacao; propor mudancas pequenas com impacto, arquivos envolvidos e riscos.'
argument-hint: 'Area/codigo que precisa de proposta de refatoracao'
user-invocable: true
---

# Refactor Impact First

## Objetivo
Produzir propostas de refatoracao aderentes ao projeto, com baixo risco de regressao e sem duplicacao desnecessaria.

## Quando usar
- Pedido de refatoracao em codigo existente.
- Pedido de melhoria de design sem mudanca de regra de negocio.
- Pedido de consolidacao de implementacoes repetidas.

## Nao usar
- Criacao de feature nova sem relacao com codigo existente.
- Mudancas exploratorias sem base em evidencias do repositorio.

## Fluxo obrigatorio antes de propor refatoracao
1. Procurar implementacoes similares no projeto.
2. Procurar interfaces ja existentes reutilizaveis.
3. Procurar padroes adotados no mesmo projeto (naming, camadas, estrutura, contratos).
4. Procurar regras de negocio afetadas direta e indiretamente.
5. Eliminar duplicacao na proposta (reuso > copia).

## Decisoes e ramificacoes
- Se existir implementacao similar confiavel: propor convergencia para o padrao existente.
- Se existir interface compativel: priorizar extensao/adaptacao em vez de criar nova interface.
- Se nao houver padrao claro: nao bloquear a proposta; declarar incerteza e sugerir menor mudanca reversivel com validacao rapida.
- Se regra de negocio puder mudar comportamento: separar refatoracao estrutural de alteracao funcional.
- Se reduzir duplicacao exigir grande impacto: propor plano em etapas pequenas com checkpoints.

## Criterios de qualidade
- A proposta explicita impacto tecnico e impacto funcional esperado.
- Os arquivos envolvidos sao listados com o proposito de cada alteracao.
- Riscos e possiveis regressos sao mapeados.
- O plano vem em passos pequenos e verificaveis.
- Nao recomenda padrao conflitante com o que ja existe no projeto sem justificativa.

## Formato de resposta
- Escopo da refatoracao: objetivo e limites.
- Impacto tecnico: estrutura, acoplamento, reuso, testabilidade e manutencao.
- Impacto funcional: comportamento que deve permanecer igual e pontos sensiveis de regressao.
- Arquivos envolvidos: lista de arquivos e proposito de cada ponto de leitura/alteracao.
- Riscos: regressao, acoplamento, compatibilidade e cobertura de testes.
- Plano incremental: passos pequenos, com validacao a cada passo.

## Checklist rapido
- implementacoes similares mapeadas
- interfaces existentes mapeadas
- padroes do projeto mapeados
- regras de negocio impactadas mapeadas
- duplicacao evitada
- impacto tecnico indicado
- impacto funcional indicado
- arquivos envolvidos com proposito indicados
- riscos indicados
- passos pequenos propostos
```

---

## 9. Realizar primeira indexação

```bash
ai-memory index --semantic
```

---

## 10. Roadmap do projeto

### MVP atual

- schema PostgreSQL;
- indexação básica;
- busca vetorial básica;
- chunking inicial com Roslyn para C#;
- extração heurística e semântica opcional de regras e conhecimento;
- comandos `index`, `search`, `watch` e `mcp`;
- servidor MCP STDIO funcional;
- ferramentas MCP `search_code`, `search_business_rules` e `find_related_files`.

### Próximas melhorias

1. Implementar watcher real com debounce.
2. Evoluir chunking C# com símbolos, relações e chamadas.
3. Evoluir extração semântica com deduplicação vetorial e agrupamento de evidências.
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
