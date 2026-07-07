# .NET 10 Migration Skills Pack

Pack interno de skills e workflows para agentes de IA trabalharem em migracoes corporativas para .NET 10.

## Origem e seguranca

Este pack nao copia skills de terceiros. Os arquivos foram escritos como prompts internos, alinhados a fontes primarias e praticas consolidadas do ecossistema .NET:

- Microsoft Learn para .NET 10, SDK, ASP.NET Core, EF Core e `dotnet test`.
- xUnit.net documentation para testes com xUnit.
- BenchmarkDotNet documentation para benchmarks.
- GitHub Actions `setup-dotnet` para workflows de CI.

## Conteudo

- `skills/dotnet10/`: 20 skills para migracao, padronizacao, testes, performance, seguranca, observabilidade, CI/CD e arquitetura.
- `workflows/dotnet10/`: templates de workflow e checklists para migracao.

## Instalacao

Depois de empacotar/instalar a tool:

```bash
ai-memory skills list
ai-memory skills detect
ai-memory skills install
ai-memory skills install --all
```

O `ai-memory setup` tambem pergunta se voce deseja instalar o pack ao final da configuracao.
