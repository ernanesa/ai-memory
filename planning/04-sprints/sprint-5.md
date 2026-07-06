# Sprint 5 — Maturidade

- **Datas**: semana 5
- **Goal**: releases automatizados; watcher real; heurísticas configuráveis; cache eficiente
- **Release alvo**: v0.5.0
- **Capacity**: ~20h/semana + possível spill de PB-013/PB-017 do Sprint 4

## Sprint Backlog

| ID | Item | Tamanho | Prioridade | Status |
|---|---|---|---|---|
| PB-018 | CI/CD (GitHub Actions) | M | P1 | To Do |
| PB-019 | Watcher real com `FileSystemWatcher` | M | P2 | To Do |
| PB-020 | Heurísticas pt-BR configuráveis | S | P3 | To Do |
| PB-021 | Cache embedding MCP LRU + TTL | S | P3 | To Do |
| (spill) PB-013 | Structured Output (se não done no Sprint 4) | M | P1 | To Do |
| (spill) PB-017 | Tray split (se não done no Sprint 4) | L | P2 | To Do |

**Total estimado**: ~16h (M+M+S+S) + spill se houver

## Sprint Goal

"Distribuir com segurança. Memória sempre fresca. Heurísticas customizáveis. Cache MCP eficiente. Pipeline CI/CD verde em cada PR."

## Critérios de aceitação do sprint

- [ ] `.github/workflows/ci.yml` roda build + testes em cada PR (Linux + macOS + Windows)
- [ ] `.github/workflows/release.yml` publica NuGet em tag `vX.Y.Z`
- [ ] `ai-memory watch` reage a edições com debounce 500ms
- [ ] Patterns de regras de negócio em arquivo de configuração (default pt-BR)
- [ ] Cache embedding MCP usa LRU com TTL (não FIFO)
- [ ] Sem warnings novos
- [ ] Release v0.5.0 publicada via pipeline

## Tasks detalhadas

### PB-018 — CI/CD

#### `.github/workflows/ci.yml`
```yaml
name: ci
on: [pull_request, push]
jobs:
  build-test:
    strategy:
      matrix:
        os: [ubuntu-latest, macos-latest, windows-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build --logger trx
      - uses: dorny/test-reporter@v1
        if: always()
        with:
          name: tests-${{ matrix.os }}
          path: '**/*.trx'
          reporter: dotnet-trx
```

#### `.github/workflows/release.yml`
```yaml
name: release
on:
  push:
    tags: ['v*']
jobs:
  pack-publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet pack -c Release -p:Version=${GITHUB_REF_NAME#v}
      - run: dotnet nuget push **/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
      - uses: softprops/action-gh-release@v2
        with:
          body_path: CHANGELOG.md
          generate_release_notes: true
```

#### CHANGELOG.md
- Criar `CHANGELOG.md` na raiz com seções `[Unreleased]`, `[0.5.0]`, etc.
- Conventional changelog format (Keep a Changelog)

#### Pré-requisitos
- Conta NuGet API key no GitHub Secrets
- Tagging manual: `git tag v0.5.0 && git push origin v0.5.0`

### PB-019 — Watcher real

#### Plano
1. `WatchCommand.cs`: usar `FileSystemWatcher`
2. Inscrever em `Changed`, `Created`, `Deleted`, `Renamed`
3. Filtro por extensões permitidas (`ChunkingService.AllowedExtensions`)
4. Ignorar `bin/`, `obj/`, `.git/`, `.idea/`, `.vs/`, `.vscode/`, `node_modules/`
5. Debounce 500ms por arquivo (evita N indexações em save sequencial)
6. Em `Changed`/`Created`: re-indexar só o arquivo alterado
7. Em `Deleted`: remover chunks órfãos do arquivo (já existe com PB-002)
8. Em `Renamed`: DELETE chunks do path antigo + indexar path novo
9. Log em tempo real: "reindexado Foo.cs"
10. Throttling: se >10 arquivos mudarem em 1s, indexar todos de uma vez

#### Teste
- Unitário: mock `FileSystemWatcher` — validar debounce
- Manual: editar arquivo no Rider, confirmar reindexação em <2s

### PB-020 — Heurísticas configuráveis

#### Plano
1. Mover predicates pt-BR de `PgVectorService.cs:9` (constantes `RuleCandidatePredicate`, `KnowledgeCandidatePredicate`, `SemanticRuleCandidatePredicate`) para arquivo JSON
2. Local: `~/.aimemory/patterns.json` ou seção em `config.json`
3. Estrutura:
   ```json
   {
     "rules": {
       "contentPatterns": ["throw new", "BusinessException", "não pode", ...],
       "symbolPatterns": ["validar", "validate", ...]
     },
     "knowledge": {
       "filePathPatterns": ["%.csproj", "%Program.cs", ...],
       "contentPatterns": ["HttpClient", "MassTransit", ...]
     }
   }
   ```
4. Defaults continuam pt-BR (compatibilidade)
5. Carregar em `ConfigService` e passar para `PgVectorService`

#### Teste
- Unitário: carregar patterns customizados e validar SQL gerado
- Manual: usuário define patterns em inglês, `index rules` encontra regras

### PB-021 — Cache LRU + TTL

#### Plano
1. Trocar `Dictionary<string, float[]>` em `McpCommand.cs:33` por `MemoryCache` (Microsoft.Extensions.Caching.Memory) ou LRU custom
2. TTL: 1 hora (configurável)
3. Tamanho máximo: 1000 entries (vs 128 hoje)
4. Thread-safe sem lock global (`MemoryCache` é thread-safe)
5. Key continua `"{model}\n{text}"`
6. Em cache miss, chama Ollama e popula

#### Pacote
- `Microsoft.Extensions.Caching.Memory` (já no ecossistema Microsoft.Extensions)

#### Teste
- Unitário: validar TTL expira entry
- Benchmark: hit-rate antes (FIFO 128) vs depois (LRU 1000 + TTL)

## Dependências

- PB-018 (CI/CD) não depende de nada
- PB-019 (watcher) depende de PB-002 (chunks órfãos) — feito em Sprint 1
- PB-020 depende de PB-007 (split) para facilitar extração de predicates — mas pode fazer sem
- PB-021 independente

## Riscos

- RS-007 ( watcher dispara em loop) — mitigar com debounce + ignores
- RS-013 (NuGet package quebra em target) — mitigar com CI em 3 OSes

## Plano de burndown ideal

```
Dia 1: PB-018 CI/CD setup + primeira pipeline verde
Dia 2: PB-019 FileSystemWatcher + debounce
Dia 3: PB-019 continuação + testes
Dia 4: PB-020 patterns configuráveis
Dia 5: PB-021 cache LRU + benchmark + release v0.5.0
```

## Sprint Review (a preenchar)

- Pipeline CI verde?
- Release v0.5.0 publicada?
- Watcher reage em <2s?

## Sprint Retrospective (a preencher)

Ver [`../07-retrospectives/sprint-5-retro.md`](../07-retrospectives/sprint-5-retro.md).
