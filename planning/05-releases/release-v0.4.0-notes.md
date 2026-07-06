# Release Notes — v0.4.0

Data: AAAA-MM-DD (após Sprint 4)
Sprint: 4
Épico(s): EP-04 (MCP 2025-11-25 + Tray split)

## Resumo

Release de upgrade MCP e desacoplamento do tray. MCP agora fala `2025-11-25` (Latest stable) com Tool Annotations, Structured Output, ícones, Tool Execution Errors e `title` field. Tray separado em package próprio (`AiMemory.Tray`), reduzindo binário MCP em ~70%.

## Breaking changes

### MCP
- Protocol version `2024-11-05` -> `2025-11-25`. **Clients antigos continuam funcionando** via version negotiation (server responde ack à versão que o client pede).

### Distribuição
- Tray agora é package separado `AiMemory.Tray` (comando `ai-memory-tray`). Usuários v0.1.5/v0.2.0/v0.3.0 com tray instalado precisam rodar `ai-memory tray update` para re-criar autostart apontando para `ai-memory-tray`.

## Migration path

```bash
# 1. Atualizar CLI/MCP
dotnet tool update -g AiMemory.Tool

# 2. Instalar tray separado
dotnet tool install -g AiMemory.Tray

# 3. Re-criar autostart apontando para ai-memory-tray
ai-memory tray update
```

Se você não usa tray (servidor/CI), pode parar no passo 1.

## Novidades

### MCP `2025-11-25`
- [PB-011] Upgrade de protocol version
- [PB-012] Tool Annotations `readOnlyHint: true` nos 7 tools — clientes pulam prompt de autorização em buscas só-leitura
- [PB-013] Structured Output com `outputSchema` tipado — IDEs renderizam cards/tabelas ao invés de JSON em texto
- [PB-014] Ícones SVG embedados em cada tool
- [PB-015] Tool Execution Errors — erros de validação de input viram `isError: true` (LLM se auto-corrigi)
- [PB-016] `title` field humano (ex.: "Search Code" em vez de "search_code")

### Tray split
- [PB-017] `AiMemory.Tray.csproj` criado com `PackageId = AiMemory.Tray`, `ToolCommandName = ai-memory-tray`
- `AiMemory.Tool` (CLI/MCP) não referencia mais Avalonia
- `ai-memory tray install` instala `AiMemory.Tray` se faltar

## Bug fixes

- LLM para em erros de input recuperáveis — agora se auto-corrigi via Tool Execution Errors

## Dívida técnica paga

- TD-008 (MCP desatualizado) — pago por PB-011 a PB-016
- TD-009 (Avalonia no projeto principal) — pago por PB-017

## Métricas

| Métrica | Antes (v0.3.0) | Depois (v0.4.0) |
|---|---|---|
| Tamanho nupkg `AiMemory.Tool` | X MB | Y MB (alvo: -70%) |
| MCP protocol version | `2024-11-05` | `2025-11-25` |
| Tools com `readOnlyHint` | 0 | 7 |
| Tools com Structured Output | 0 | 7 |
| Tools com ícone | 0 | 7 |
| Assemblies carregados por `ai-memory mcp` | N (com Avalonia) | N-X (sem Avalonia) |

## Testado em

- [ ] Rider
- [ ] Cursor
- [ ] Claude Desktop
- [ ] Codex
- [ ] Linux (autostart `.desktop`)
- [ ] macOS (LaunchAgent)
- [ ] Windows 11 (Startup folder `.lnk`)
- [ ] dotnet build -c Release (ambos projetos)
- [ ] dotnet test

## ADRs relacionados

- [ADR-001] Upgrade MCP para `2025-11-25` (não `2025-06-18`)
- [ADR-002] Não adotar MCP `2026-07-28` RC até virar stable
- [ADR-004] Separar Tray em projeto/package próprio

## Notas

- Se você usa tray e esquecer de rodar `ai-memory tray update`, o autostart continuará apontando para o `ai-memory` antigo. A CLI antiga ainda funciona, mas não receberá mais updates do tray.
- Para desinstalar completamente: `ai-memory tray remove && dotnet tool uninstall -g AiMemory.Tray && dotnet tool uninstall -g AiMemory.Tool`
