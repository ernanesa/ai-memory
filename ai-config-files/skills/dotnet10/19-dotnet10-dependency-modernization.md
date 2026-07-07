---
name: dotnet10-dependency-modernization
description: 'Modernizacao de dependencias NuGet para .NET 10. Use para revisar pacotes, central package management, vulnerabilidades e compatibilidade.'
argument-hint: 'csproj, Directory.Packages.props ou lista de pacotes'
user-invocable: true
---

# Modernizacao de Dependencias .NET 10

## Objetivo

Atualizar dependencias com controle de risco, sem criar um carnaval de versoes impossivel de manter.

## Fluxo obrigatorio

1. Mapear pacotes diretos, transitivos e criticos.
2. Identificar pacotes obsoletos, abandonados, vulneraveis ou incompatíveis.
3. Atualizar por grupos pequenos e testaveis.
4. Preferir central package management em repositorios grandes.
5. Registrar decisao quando manter versao antiga.

## Nao fazer

- Nao atualizar tudo em um unico PR sem necessidade.
- Nao trocar biblioteca por alternativa sem custo/beneficio.
- Nao remover pacote transitivo sem entender origem.

## Saida esperada

- Pacotes por prioridade.
- Riscos.
- Ordem de atualizacao.
- Testes necessarios.
