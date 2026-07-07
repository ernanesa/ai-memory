# Visão do Produto

## Elevator pitch

**Para** times de engenharia .NET que usam assistentes de IA em IDEs
**o ai-memory é** uma memória local de engenharia que indexa código, regras de negócio e conhecimento arquitetural
**que** expõe via MCP (Model Context Protocol) para Rider, VS Code, Cursor e Codex
**diferentemente** de RAGs genéricos, o ai-memory usa Roslyn para entender a estrutura do C#, mantém um grafo de símbolos navegável e comprime contexto automaticamente para reduzir custo de tokens.

## Objetivos de produto (12 meses)

1. **Reduzir tokens enviados ao LLM em 60–90%** em sessões de engenharia .NET.
2. **Indexar múltiplos workspaces/projetos** com relacionamento many-to-many.
3. **Extrair regras de negócio e conhecimento técnico** com confiança e evidência.
4. **Integrar IDEs por MCP** com ferramentas só-leitura de busca e navegação de grafo.
5. **Funcionar 100% local**, sem nuvem, sem vendor lock-in.
6. **Distribuir como .NET global tool** via NuGet com setup interativo cross-platform.

## Não-objetitos (desta fase)

- Otimização de latência via ONNX embutido, HNSW, cache persistente — postergada (ver ADR-003).
- Adição de novas linguagens (TypeScript, Python, Java) — foco .NET/C#/.razor/.cshtml.
- Multi-tenant / SaaS / nuvem.
- Treinamento de modelos.

## Métricas de sucesso

| Métrica | Baseline hoje | Alvo 12 meses |
|---|---|---|
| Tokens por sessão MCP (mediana) | desconhecido | -60% vs baseline |
| Latência `search_code` (p95) | 60–230 ms | <50 ms (sem otimização de latência, via cache FIFO->LRU + paralelismo) |
| Cobertura de testes | 0% | >70% em serviços core |
| Tempo de indexação de 100k chunks | ~horas | <30 min (batch embedding + paralelismo) |
| Build em CI | não existe | verde em cada PR |
| Releases NuGet | manual | automatizado em tag |
| Versão MCP | `2024-11-05` | `2025-11-25` em v0.4.0; `2026-07-28` quando stable |
| Bugs silenciosos conhecidos | chunks órfãos, versão de modelo não fixada | zero conhecidos |

## Personas-alvo

Ver [`personas.md`](personas.md).

## Problemas atuais

Ver [`problem-statement.md`](problem-statement.md).

## Histórico

Ver [`history.md`](history.md).
