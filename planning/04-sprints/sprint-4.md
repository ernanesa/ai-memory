# Sprint 4 — MCP 2025-11-25 + Tray split

- **Datas**: semana 4
- **Goal**: MCP alinhado ao spec mais recente estável; tray desacoplado da CLI
- **Release alvo**: v0.4.0
- **Capacity**: ~20h/semana

## Sprint Backlog

| ID | Item | Tamanho | Prioridade | Status |
|---|---|---|---|---|
| PB-011 | Upgrade MCP `2024-11-05` -> `2025-11-25` | S | P1 | To Do |
| PB-012 | Tool Annotations (`readOnlyHint: true`) | XS | P1 | To Do |
| PB-013 | Structured Output com `outputSchema` | M | P1 | To Do |
| PB-014 | Ícones em tools | XS | P2 | To Do |
| PB-015 | Tool Execution Errors | S | P1 | To Do |
| PB-016 | `title` field | XS | P2 | To Do |
| PB-017 | Tray split em projeto próprio | L | P2 | To Do |

**Total estimado**: ~25h — sprint apertado, considerar mover PB-013 ou PB-017 para Sprint 5

## Sprint Goal

"MCP fala `2025-11-25`. Clientes pulam autorização em tools só-leitura. Resultados renderizados estruturados. Tray desacoplado — binário MCP ~70% menor."

## Critérios de aceitação do sprint

- [ ] `McpCommand.cs:10` com `ProtocolVersion = "2025-11-25"`
- [ ] 7 tools com `annotations.readOnlyHint = true`
- [ ] 7 tools com `outputSchema` tipado
- [ ] 7 tools com `title` humano
- [ ] 7 tools com `icons` SVG embedado
- [ ] `ArgumentException` vira Tool Execution Error (`isError: true` em `content[]`)
- [ ] `AiMemory.Tray.csproj` criado, `AiMemory.Tool` sem referência Avalonia
- [ ] `ai-memory tray install` instala `AiMemory.Tray` se faltar
- [ ] Teste manual em Rider/Cursor confirma ausência de prompt de autorização
- [ ] Tamanho do nupkg `AiMemory.Tool` medido antes/depois (alvo: -70%)

## Tasks detalhadas

### PB-011 — Upgrade MCP

- `McpCommand.cs:10`: `ProtocolVersion = "2025-11-25"`
- Validar `InitializeResult` em `McpCommand.cs:136` com novos campos:
  - `serverInfo.title` (human-friendly name)
  - `serverInfo.description` (introduzido em 2025-11-25)
  - `capabilities.extensions` (se aplicável)
- Version negotiation atual (`McpCommand.cs:138-145`) já aceita versão do client — manter
- Testar com Rider, Cursor, Claude Desktop — todos devem conectar

### PB-012 — Tool Annotations

- `McpCommand.cs:352` (`CreateTools()`): adicionar em cada tool:
  ```csharp
  annotations = new
  {
      readOnlyHint = true,
      // destructiveHint = false (default),
      // idempotentHint = true (buscas são idempotentes),
      // openWorldHint = false (escopo fechado do repo indexado)
  }
  ```
- Todos os 7 tools são só-leitura (nenhum escreve no repo)
- Teste manual: Cursor não pede confirmação em `search_code`

### PB-013 — Structured Output

- Para cada tool, definir `outputSchema` (JSON Schema 2020-12) tipando o result
- `ToolJsonResult` atual serializa em `content[].text` — adicionar `structuredContent`:
  ```csharp
  return new
  {
      content = new[] { new { type = "text", text = jsonText } },
      structuredContent = payload
  };
  ```
- Schemas:
  - `search_code`: `CodeSearchResult[]` (project, file, language, chunkType, symbol, distance, content)
  - `search_business_rules`: `BusinessRuleSearchResult[]`
  - `search_knowledge`: `KnowledgeSearchResult[]`
  - `find_related_files`: `RelatedFileResult[]`
  - `get_symbol_callers`/`callees`: `(Project, Symbol, File, Relation)[]`
  - `get_class_hierarchy`: `(Project, ParentName, Relation)[]`
- Manter `content[].text` para clients antigos (backward compat)

### PB-014 — Ícones

- Embedar SVGs como `data:image/svg+xml;base64,...`
- Sugestões:
  - `search_code`, `search_business_rules`, `search_knowledge`: ícone de cérebro/lupa
  - `find_related_files`: ícone de arquivos conectados
  - `get_symbol_callers`, `get_symbol_callees`: ícone de setas bidirecionais
  - `get_class_hierarchy`: ícone de árvore
- Assets em `Tray/Assets/` já têm ícone do projeto — reusar quando possível

### PB-015 — Tool Execution Errors

- `McpCommand.cs:124`: hoje
  ```csharp
  catch (ArgumentException ex) { await WriteToolErrorAsync(id, ex.Message, ct); }
  catch (Exception ex) { ... await WriteToolErrorAsync(id, ex.Message, ct); }
  ```
- Mudar para Tool Execution Error (per spec 2025-06-18+):
  ```csharp
  catch (ArgumentException ex)
  {
      await WriteResultAsync(id, new
      {
          content = new[] { new { type = "text", text = ex.Message } },
          isError = true
      }, ct);
  }
  ```
- Protocol errors (JSON parse, method not found) continuam como `-32600`/`-32601`
- LLM vê `isError: true` e tenta novamente com args diferentes

### PB-016 — `title` field

- `McpCommand.cs:352`: adicionar em cada tool:
  - `search_code` -> `title = "Search Code"`
  - `search_business_rules` -> `title = "Search Business Rules"`
  - `search_knowledge` -> `title = "Search Knowledge"`
  - `find_related_files` -> `title = "Find Related Files"`
  - `get_symbol_callers` -> `title = "Get Symbol Callers"`
  - `get_symbol_callees` -> `title = "Get Symbol Callees"`
  - `get_class_hierarchy` -> `title = "Get Class Hierarchy"`

### PB-017 — Tray split

#### Estrutura
- `AiMemory.Tray.csproj` (novo projeto na raiz, não dentro do `ai-memory.csproj`)
  - `PackageId = AiMemory.Tray`
  - `ToolCommandName = ai-memory-tray`
  - Referência Avalonia
  - Copiar `Tray/App.axaml*` e `Tray/Assets/` para o novo projeto
- `ai-memory.csproj`:
  - Remover `PackageReference` Avalonia
  - Remover `<AvaloniaResource Include="Tray/Assets/**" />`
  - Remover `<Compile Remove="AiMemory.Tray/**" />` (não precisa mais)

#### `ai-memory tray install`
- Em `TraySetupService.InstallAsync`:
  - Detectar se `ai-memory-tray` está instalado (`dotnet tool list -g | grep ai-memory-tray`)
  - Se não, rodar `dotnet tool install -g AiMemory.Tray`
  - Apontar autostart para `ai-memory-tray` (não `ai-memory` interno)

#### Migration path para usuários v0.1.5
- Release notes v0.4.0 documentam:
  1. `dotnet tool update -g AiMemory.Tool`
  2. `ai-memory tray update` (re-cria autostart apontando para `ai-memory-tray`)
  3. Se `ai-memory-tray` não instalado, `ai-memory tray install` instala
- `tray update` detecta autostart antigo (apontando para `ai-memory`) e re-cria

#### Validação
- `dotnet pack -c Release` em `ai-memory.csproj` gera `AiMemory.Tool.X.Y.Z.nupkg` sem Avalonia
- `dotnet pack -c Release` em `AiMemory.Tray.csproj` gera `AiMemory.Tray.X.Y.Z.nupkg`
- Tamanho dos nupkgs medido antes/depois
- Autostart funciona em Linux/macOS/Windows

## Dependências

- PB-011 é pré-requisito de PB-012, PB-013, PB-014, PB-015, PB-016
- PB-017 independente

## Riscos

- RS-001 (clients não suportam 2025-11-25) — mitigar com version negotiation
- RS-006 (tray split quebra autostart) — mitigar com `tray update` e release notes

## Plano de burndown ideal

```
Dia 1: PB-011 upgrade + PB-012 annotations + PB-016 title (rápidos)
Dia 2: PB-015 Tool Execution Errors + PB-014 ícones
Dia 3: PB-013 Structured Output (maior)
Dia 4: PB-017 Tray split (começar)
Dia 5: PB-017 continuação + testes manuais em clients
```

Se PB-013 ou PB-017 não terminarem, mover para Sprint 5.

## Sprint Review (a preencher)

- Clients testados:
- Tamanho nupkg antes/depois:
- Demo realizada:

## Sprint Retrospective (a preencher)

Ver [`../07-retrospectives/sprint-4-retro.md`](../07-retrospectives/sprint-4-retro.md).
