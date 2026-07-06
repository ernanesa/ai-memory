# Sprint 3 — Performance

- **Datas**: semana 3
- **Goal**: indexação 10× mais rápida; extração paralela; resiliência a falhas transitórias
- **Release alvo**: v0.3.0
- **Capacity**: ~20h/semana

## Sprint Backlog

| ID | Item | Tamanho | Prioridade | Status |
|---|---|---|---|---|
| PB-008 | Batch embedding no Ollama | M | P1 | To Do |
| PB-009 | Paralelizar extração `rules`/`knowledge` | M | P1 | To Do |
| PB-010 | Retry/backoff no Ollama (Polly) | S | P2 | To Do |

**Total estimado**: ~16h (M+M+S)

## Sprint Goal

"Indexar 100k chunks em <30 min. Extração semântica aproveita múltiplos cores. Falhas transitórias do Ollama não abortam pipeline."

## Critérios de aceitação do sprint

- [ ] `OllamaService.EmbedBatchAsync(IEnumerable<string>)` implementado
- [ ] `IndexCommand.IndexChunksAsync` usa batch (K chunks por request)
- [ ] `IndexRulesAsync` e `IndexKnowledgeAsync` usam `Parallel.ForEachAsync` com `--parallelism`
- [ ] Polly retry 3x com backoff exponencial + jitter em `OllamaService`
- [ ] Benchmark em repo de referência (ex.: claps) documenta speedup
- [ ] Progress panel funciona corretamente com concorrência
- [ ] Sem warnings novos

## Tasks detalhadas

### PB-008 — Batch embedding

#### Plano
1. Adicionar método `EmbedBatchAsync(IEnumerable<string> inputs, CancellationToken ct)` em `OllamaService`
2. POST `/api/embed` (novo endpoint Ollama) com `{ model, input: [...] }` — retorna array de embeddings
3. Se Ollama antigo não suporta `/api/embed`, fallback para `/api/embeddings` N vezes
4. Em `IndexCommand.IndexChunksAsync` (linha 149):
   - Agrupar chunks por arquivo em batches de K (K=16 inicial)
   - Chamar `EmbedBatchAsync` uma vez por batch
   - Upsert cada chunk individualmente (ou batch upsert se Npgsql suportar)
5. Em erro de batch, retentar chunks individualmente (RS-005)

#### Pacotes
- Sem dependência nova (já usa `System.Net.Http.Json`)

#### Teste
- Unitário: mock HttpClient, validar payload `{ model, input: [...] }`
- Integração: comparar embeddings de 1 chunk via batch vs single

### PB-009 — Paralelizar extração

#### Plano
1. Em `IndexCommand.IndexRulesAsync` (linha 239) e `IndexKnowledgeAsync` (linha 358):
   - Trocar `foreach` por `await Parallel.ForEachAsync(chunks, new ParallelOptions { MaxDegreeOfParallelism = parallelism ?? 4 }, async (chunk, ct) => { ... })`
2. `ProgressPanel` precisa ser thread-safe (já usa `lock _gate` — confirmar)
3. `ai_extraction_chunk_state` updates já são por chunk — sem race
4. Variáveis `inserted`, `updated`, `skipped` usam `Interlocked.Increment`/`Add`
5. `--parallelism` já existe como option em `Program.cs:18` — propagar para `IndexRulesAsync`/`IndexKnowledgeAsync`

#### Teste
- Unitário: chamadas concorrentes a um mock repository — estado final consistente
- Integração: rodar `index rules --semantic --parallelism 4` em repo pequeno — comparar com `--parallelism 1`

### PB-010 — Retry/backoff Polly

#### Plano
1. Adicionar `PackageReference Include="Polly"` (~v8) em `ai-memory.csproj`
2. Em `OllamaService`:
   - Criar `AsyncRetryPolicy` com 3 retentativas, backoff exponencial + jitter (ex.: 1s, 2s, 4s + random)
   - Envolver `EmbedAsync`, `EmbedBatchAsync`, `GenerateJsonAsync`
3. Após 3 falhas, propagar exceção (caller marca chunk `failed`)
4. Log de retry em `Console.Error` (não stdout)

#### Teste
- Unitário: mock HttpClient retornando 503 2x depois 200 — policy retenta e succeeds
- Unitário: mock HttpClient retornando 503 4x — policy falha após 3 retentativas

## Métricas de benchmark

Definir repo de referência (ex.: `claps` com X chunks). Antes do sprint, medir:
- Tempo de `ai-memory index chunks` (sem semântica)
- Tempo de `ai-memory index rules --semantic`
- Tempo de `ai-memory index knowledge --semantic`

Após sprint, medir novamente. Documentar em `planning/05-releases/release-v0.3.0-notes.md`.

## Dependências

- PB-007 (split) facilita PB-008 e PB-009 (repositories testáveis) — mas não bloqueia
- Se PB-007 spillou de Sprint 2, reconsiderar ordem

## Riscos

- RS-004 (race condition) — mitigar com `Interlocked` e teste de concorrência
- RS-005 (batch perde chunks) — mitigar com retry individual

## Plano de burndown ideal

```
Dia 1: PB-010 Polly setup + testes
Dia 2: PB-008 EmbedBatchAsync + testes
Dia 3: PB-008 integração em IndexCommand + benchmark
Dia 4: PB-009 Parallel.ForEachAsync + testes
Dia 5: benchmark final, review, retrospective
```

## Sprint Review (a preencher)

- Speedup chunks: Xx (vs baseline)
- Speedup rules: Xx
- Speedup knowledge: Xx

## Sprint Retrospective (a preencher)

Ver [`../07-retrospectives/sprint-3-retro.md`](../07-retrospectives/sprint-3-retro.md).
