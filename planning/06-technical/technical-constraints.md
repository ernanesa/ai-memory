# Restrições Técnicas

Limites e premissas dentro dos quais o projeto opera.

## Stack fixa

- **Linguagem**: C# (.NET 10)
- **Banco**: PostgreSQL 14+ com pgvector
- **Embedding**: Ollama + bge-m3 (1024 dims)
- **LLM para extração semântica**: Ollama + qwen2.5-coder:7b
- **MCP transport**: STDIO (não HTTP nesta fase)
- **Tray UI**: Avalonia 11
- **Distribuição**: .NET global tool via NuGet

## Plataformas suportadas

- Linux (Ubuntu, BigLinux, Debian-derivados)
- macOS
- Windows 11

## Não-negociáveis (hard constraints)

- **Local-first**: sem nuvem, sem vendor lock-in. Tudo roda na máquina do usuário.
- **Sem treino de modelo**: o projeto cria memória pesquisável, não treina LLM.
- **Sem autenticação OAuth no MCP**: uso local, STDIO.
- **MCP transport STDIO**: não expor via HTTP nesta fase (ver ADR-002 para futuro).
- **Foco .NET**: não adicionar suporte a TypeScript/Python/Java nesta fase.
- **Sem comentários em código** salvo "por quê" não-óbvio (segue working agreement).

## Soft constraints (flexíveis)

- **Tamanho máximo de chunk**: ~6000 chars (configurável)
- **Parallelismo default**: `Math.Clamp(ProcessorCount / 2, 2, 6)`
- **Cache embedding MCP**: 128 entries (subir para 1000 com LRU em PB-021)
- **TTL cache**: 1 hora (configurável)

## Dependências externas

| Dependência | Versão atual | Lock em | Risco |
|---|---|---|---|
| Npgsql | 9.0.3 | `ai-memory.csproj` | Baixo |
| Pgvector | 0.3.2 | `ai-memory.csproj` | Baixo |
| Microsoft.Extensions.Hosting | 10.0.0 | `ai-memory.csproj` | Baixo |
| Microsoft.CodeAnalysis.CSharp | 5.0.0 | `ai-memory.csproj` | Médio (Roslyn evolves) |
| System.CommandLine | 2.0.0-beta4 | `ai-memory.csproj` | Médio (beta) |
| Avalonia | 11.0.10 | `ai-memory.csproj` | Médio (será movido em PB-017) |
| Ollama | externo | n/a | Médio (API pode mudar) |
| PostgreSQL + pgvector | externo | n/a | Baixo |

## Requisitos de runtime

- **RAM mínima**: 4 GB (Postgres + Ollama + IDE); 8 GB recomendada; 16 GB confortável
- **Disco**: ~2 GB para modelos Ollama (bge-m3 1.2GB + qwen2.5-coder 4.7GB)
- **CPU**: 2 cores mínimo; 4+ cores aceleram indexação (paralelismo)

## Requisitos de desenvolvimento

- .NET 10 SDK
- Docker (para Testcontainers em testes de integração)
- PostgreSQL local OU Testcontainers
- Ollama local (para teste manual de embedding/semântica)

## Limites conhecidos

- **Sem índice HNSW**: busca O(n); aceitável para <100k chunks (ver ADR-003)
- **Sem cache persistente**: embeddings recomputados em restart (ver ADR-003)
- **Sem ONNX embutido**: dependência Ollama para embedding (ver ADR-003)
- **Watcher não é real**: precisa `index` manual até PB-019
- **MCP `2024-11-05`**: será `2025-11-25` em v0.4.0 (PB-011)
- **Tray no package principal**: será separado em v0.4.0 (PB-017)

## Padrões de código

- File-scoped namespaces
- Nullable enabled
- `sealed` em classes não-derivadas
- `record` para DTOs
- `IAsyncDisposable` em serviços com recursos
- Async/await em toda I/O
- Sem `Console.WriteLine` em serviços chamados pelo MCP (use `Console.Error`)

## Padrões de SQL

- `CREATE TABLE IF NOT EXISTS`
- `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`
- `CREATE INDEX IF NOT EXISTS`
- `ON DELETE CASCADE` em FKs de chunks
- Migrations aditivas (nunca destrutivas sem script de rollback)
- Hash SHA256 registrado em `ai_schema_migrations`
