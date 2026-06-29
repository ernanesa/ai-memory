using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiMemory.Services;

public sealed class SymbolGraphService
{
    private readonly PgVectorService _pg;
    private readonly int _projectId;
    private readonly string _projectRoot;

    public SymbolGraphService(PgVectorService pg, int projectId, string projectRoot)
    {
        _pg = pg;
        _projectId = projectId;
        _projectRoot = projectRoot;
    }

    public async Task BuildGraphAsync(IEnumerable<string> files)
    {
        var csFiles = files.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList();
        var symbols = new List<LocalSymbol>();

        // Passo 1: Extrair e salvar todos os símbolos declarados
        foreach (var file in csFiles)
        {
            try
            {
                var text = await File.ReadAllTextAsync(file);
                var tree = CSharpSyntaxTree.ParseText(text);
                var root = tree.GetCompilationUnitRoot();
                var relativePath = Path.GetRelativePath(_projectRoot, file);

                // Encontrar classes, interfaces, structs, enums, records
                var typeDeclarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>();
                foreach (var typeDecl in typeDeclarations)
                {
                    var namespaceName = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
                    var typeName = typeDecl.Identifier.ValueText;
                    var fullTypeName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
                    
                    var kind = typeDecl switch
                    {
                        ClassDeclarationSyntax => "class",
                        InterfaceDeclarationSyntax => "interface",
                        StructDeclarationSyntax => "struct",
                        EnumDeclarationSyntax => "enum",
                        RecordDeclarationSyntax => "record",
                        _ => "type"
                    };

                    var lineSpan = typeDecl.GetLocation().GetLineSpan();
                    var startLine = lineSpan.StartLinePosition.Line + 1;
                    var endLine = lineSpan.EndLinePosition.Line + 1;

                    // Buscar chunk_id correspondente no banco (tentativa heurística pelo symbol_name)
                    var chunkId = await _pg.GetChunkIdBySymbolNameAsync(_projectId, fullTypeName);

                    LocalSymbol? localSymbol = null;
                    var symbolId = await _pg.UpsertSymbolAsync(
                        _projectId,
                        chunkId,
                        kind,
                        fullTypeName,
                        relativePath,
                        startLine,
                        endLine
                    );

                    if (symbolId.HasValue)
                    {
                        localSymbol = new LocalSymbol(symbolId.Value, kind, fullTypeName, typeDecl);
                        symbols.Add(localSymbol);
                    }

                    // Encontrar membros da classe/tipo
                    if (typeDecl is TypeDeclarationSyntax typeDeclSyntax)
                    {
                        foreach (var member in typeDeclSyntax.Members)
                        {
                            if (member is MethodDeclarationSyntax methodDecl)
                            {
                                var methodName = methodDecl.Identifier.ValueText;
                                var fullMethodName = $"{fullTypeName}.{methodName}";
                                var methodLineSpan = methodDecl.GetLocation().GetLineSpan();
                                var methodStart = methodLineSpan.StartLinePosition.Line + 1;
                                var methodEnd = methodLineSpan.EndLinePosition.Line + 1;

                                var methodChunkId = await _pg.GetChunkIdBySymbolNameAsync(_projectId, $"{fullTypeName}.{methodName}({string.Join(",", methodDecl.ParameterList.Parameters.Select(p => p.Type?.ToString()))})");
                                methodChunkId ??= await _pg.GetChunkIdBySymbolNameAsync(_projectId, methodName);

                                var methodSymbolId = await _pg.UpsertSymbolAsync(
                                    _projectId,
                                    methodChunkId,
                                    "method",
                                    fullMethodName,
                                    relativePath,
                                    methodStart,
                                    methodEnd
                                );

                                if (methodSymbolId.HasValue)
                                {
                                    symbols.Add(new LocalSymbol(methodSymbolId.Value, "method", fullMethodName, methodDecl, localSymbol));
                                }
                            }
                            else if (member is PropertyDeclarationSyntax propDecl)
                            {
                                var propName = propDecl.Identifier.ValueText;
                                var fullPropName = $"{fullTypeName}.{propName}";
                                var propLineSpan = propDecl.GetLocation().GetLineSpan();
                                var propStart = propLineSpan.StartLinePosition.Line + 1;
                                var propEnd = propLineSpan.EndLinePosition.Line + 1;

                                var propSymbolId = await _pg.UpsertSymbolAsync(
                                    _projectId,
                                    null,
                                    "property",
                                    fullPropName,
                                    relativePath,
                                    propStart,
                                    propEnd
                                );

                                if (propSymbolId.HasValue)
                                {
                                    symbols.Add(new LocalSymbol(propSymbolId.Value, "property", fullPropName, propDecl, localSymbol));
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignorar arquivos corrompidos ou falhas de parsing roslyn local
            }
        }

        // Passo 2: Construir e salvar relações
        foreach (var sym in symbols)
        {
            try
            {
                if (sym.Node is BaseTypeDeclarationSyntax typeDecl)
                {
                    // 1. Relações de herança/implementação de interface (inherits/implements)
                    if (typeDecl.BaseList is not null)
                    {
                        foreach (var baseType in typeDecl.BaseList.Types)
                        {
                            var baseName = baseType.Type.ToString();
                            // Procurar o símbolo correspondente à classe base
                            var baseSymbol = symbols.FirstOrDefault(s => s.Kind is "class" or "interface" && s.Name.EndsWith(baseName));
                            if (baseSymbol is not null)
                            {
                                var relation = baseSymbol.Kind == "interface" ? "implements" : "inherits";
                                await _pg.UpsertSymbolRelationAsync(sym.Id, baseSymbol.Id, relation);
                            }
                        }
                    }
                }
                else if (sym.Node is MethodDeclarationSyntax methodDecl)
                {
                    // 2. Relações de chamadas de método (calls)
                    var invocations = methodDecl.DescendantNodes().OfType<InvocationExpressionSyntax>();
                    foreach (var invoke in invocations)
                    {
                        var invokedName = GetInvokedMethodName(invoke);
                        if (string.IsNullOrEmpty(invokedName)) continue;

                        // Procurar se é um método local no próprio projeto
                        var targetMethod = symbols.FirstOrDefault(s => s.Kind == "method" && s.Name.EndsWith("." + invokedName));
                        if (targetMethod is not null)
                        {
                            await _pg.UpsertSymbolRelationAsync(sym.Id, targetMethod.Id, "calls");
                        }
                    }
                }
            }
            catch
            {
                // Ignorar falhas de resolução de relacionamento individual
            }
        }
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax invoke)
    {
        if (invoke.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.ValueText;
        }
        else if (invoke.Expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.ValueText;
        }
        return null;
    }

    private sealed class LocalSymbol
    {
        public Guid Id { get; }
        public string Kind { get; }
        public string Name { get; }
        public SyntaxNode Node { get; }
        public LocalSymbol? Parent { get; }

        public LocalSymbol(Guid id, string kind, string name, SyntaxNode node, LocalSymbol? parent = null)
        {
            Id = id;
            Kind = kind;
            Name = name;
            Node = node;
            Parent = parent;
        }
    }
}
