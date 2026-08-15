using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ChangeLens.Infrastructure.Analysis;

/// <summary>One source file fed to the analyzer (content in, graph out — never code execution).</summary>
public sealed record SourceFile(string Path, string Content);

/// <summary>Result of analyzing one repository state: symbols, edges, graph, and a summary.</summary>
public sealed record RepositoryAnalysis(
    DependencyGraph Graph,
    int ProjectCount,
    int FileCount,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Graph summary used for verification, e.g. "Projects: 5 Files: 24 Classes: 30 …".</summary>
    public string Summary
    {
        get
        {
            var nodes = Graph.Nodes.Values;
            var kinds = nodes.GroupBy(s => s.Kind)
                .ToDictionary(g => g.Key, g => g.Count());
            int Count(SymbolKind k) => kinds.TryGetValue(k, out var n) ? n : 0;

            return $"Projects: {ProjectCount} Files: {FileCount} " +
                   $"Classes: {Count(SymbolKind.Class)} Interfaces: {Count(SymbolKind.Interface)} " +
                   $"Methods: {Count(SymbolKind.Method)} Constructors: {Count(SymbolKind.Constructor)} " +
                   $"Properties: {Count(SymbolKind.Property)} Dependency edges: {Graph.Edges.Count}";
        }
    }
}

/// <summary>
/// Roslyn-based C# analyzer (ADR-0011): semantic symbol extraction + dependency edges.
/// The analyzer only parses/analyzes source — it never executes code from the repository.
/// Missing/irrelevant references produce warnings, never failures.
/// </summary>
public sealed class RoslynAnalyzer
{
    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly ImmutableArray<MetadataReference> SharedReferences = BuildReferences();

    /// <summary>Analyze one repository state (all project files as in-memory source).</summary>
    public RepositoryAnalysis Analyze(IReadOnlyList<SourceFile> files)
    {
        var warnings = new List<string>();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        var trees = files
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.Path))
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            "ChangeLensAnalysis",
            trees,
            SharedReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));

        // Repo-declared symbols are those whose declaration lives in one of our trees.
        var treePaths = trees.Select(t => t.FilePath).ToHashSet(StringComparer.Ordinal);

        var nodes = new Dictionary<string, SymbolInfo>(StringComparer.Ordinal);
        var edges = new HashSet<DependencyEdge>();
        var projects = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var typeNode in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeNode) is not INamedTypeSymbol type || !IsRepoDeclared(type, treePaths))
                {
                    continue;
                }

                AddNode(nodes, ToSymbol(type, type.Locations.FirstOrDefault()?.SourceTree?.FilePath));
                projects.Add(DeriveProject(type.Locations.FirstOrDefault()?.SourceTree?.FilePath));

                // Primary-constructor parameters (no ConstructorDeclarationSyntax node exists
                // for them) — their types are references from the class to repo types.
                foreach (var primaryCtor in type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared))
                {
                    foreach (var paramType in primaryCtor.Parameters.Select(p => p.Type))
                    {
                        if (paramType is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Interface }
                            && IsRepoDeclared(paramType, treePaths))
                        {
                            edges.Add(new DependencyEdge(
                                type.ToDisplayString(FullyQualified),
                                paramType.ToDisplayString(FullyQualified),
                                EdgeType.ReferencesType,
                                tree.FilePath));
                        }
                    }
                }

                // Inheritance + interface implementation (only when the base is repo-declared).
                foreach (var baseRef in typeNode.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
                {
                    if (model.GetTypeInfo(baseRef.Type).Type is not INamedTypeSymbol baseType || !IsRepoDeclared(baseType, treePaths))
                    {
                        continue;
                    }

                    var edgeType = baseType.TypeKind == TypeKind.Interface ? EdgeType.Implements : EdgeType.Inherits;
                    edges.Add(new DependencyEdge(
                        type.ToDisplayString(FullyQualified),
                        baseType.ToDisplayString(FullyQualified),
                        edgeType,
                        tree.FilePath));
                }

                // Top-level statements (Program.cs style, e.g. DI registrations such as
                // AddSingleton<TokenService>()) — attribute references to the type so the
                // graph captures registrations that have no method-declaration wrapper.
                CollectTopLevelTypeReferences(type, tree.FilePath, treePaths, model, edges);
            }

            foreach (var memberNode in root.DescendantNodes()
                         .Where(n => n is MethodDeclarationSyntax
                             or ConstructorDeclarationSyntax
                             or PropertyDeclarationSyntax
                             or FieldDeclarationSyntax
                             or DestructorDeclarationSyntax))
            {
                // A FieldDeclarationSyntax may declare several variables; resolve each.
                IEnumerable<ISymbol> declared = memberNode switch
                {
                    FieldDeclarationSyntax field => field.Declaration.Variables
                        .Select(v => model.GetDeclaredSymbol(v))
                        .OfType<ISymbol>(),
                    _ => model.GetDeclaredSymbol(memberNode) is { } s ? [s] : []
                };

                foreach (var symbol in declared)
                {
                    if (symbol is null || !IsRepoDeclared(symbol, treePaths))
                    {
                        continue;
                    }

                    var containingType = symbol.ContainingType;
                    if (containingType is null)
                    {
                        continue;
                    }

                    var containerFqn = containingType.ToDisplayString(FullyQualified);
                    var file = symbol.Locations.FirstOrDefault()?.SourceTree?.FilePath;

                    switch (symbol)
                    {
                        case IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } method:
                            AddNode(nodes, ToSymbol(method, file));
                            break;
                        case IMethodSymbol method:
                            AddNode(nodes, ToSymbol(method, file));
                            CollectCallEdges(method, method, file, treePaths, model, edges);
                            break;
                        case IPropertySymbol property:
                            AddNode(nodes, ToSymbol(property, file));
                            break;
                        case IFieldSymbol field:
                            AddNode(nodes, ToSymbol(field, file));
                            break;
                    }

                    if (symbol is IMethodSymbol methodOrCtor)
                    {
                        CollectTypeReferenceEdges(methodOrCtor, containerFqn, file, treePaths, model, edges);
                    }
                }
            }
        }

        var graph = new DependencyGraph(nodes, edges.OrderBy(e => e.FromSymbolId, StringComparer.Ordinal).ToList());
        var diagnostics = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(20).ToList();
        if (diagnostics.Count > 0)
        {
            warnings.Add($"Roslyn compilation produced {compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error)} errors (unresolved references are expected); first: {diagnostics[0]}");
        }

        return new RepositoryAnalysis(graph, projects.Count, files.Count, warnings);
    }

    private static void AddNode(Dictionary<string, SymbolInfo> nodes, SymbolInfo? symbol)
    {
        if (symbol is not null)
        {
            nodes.TryAdd(symbol.SymbolId, symbol);
        }
    }

    private static SymbolInfo? ToSymbol(INamedTypeSymbol type, string? file)
        => new(
            SymbolId: type.ToDisplayString(FullyQualified),
            Kind: type.TypeKind switch
            {
                TypeKind.Interface => SymbolKind.Interface,
                TypeKind.Struct => SymbolKind.Struct,
                TypeKind.Enum => SymbolKind.Enum,
                _ => SymbolKind.Class
            },
            Name: type.Name,
            FullyQualifiedName: type.ToDisplayString(FullyQualified),
            FilePath: file,
            Namespace: type.ContainingNamespace?.IsGlobalNamespace == false
                ? type.ContainingNamespace.ToDisplayString()
                : null,
            Project: DeriveProject(file),
            // Declaration header up to the body brace: carries attributes ([Route(...)])
            // and primary-constructor shape (e.g. a class taking an HttpClient).
            Signature: DeclarationHeader(type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()),
            ReturnType: null,
            Parameters: Array.Empty<string>(),
            DeclarationHash: DeclarationHash(type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()));

    private static SymbolInfo? ToSymbol(IMethodSymbol method, string? file)
    {
        // FullyQualifiedFormat does NOT qualify member names (a Roslyn quirk: it renders
        // methods as just "Process") — build a truly unique id manually:
        // containingTypeFqn.methodName(paramTypeFqns).
        var parameters = string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(FullyQualified)));
        var fqn = $"{method.ContainingType.ToDisplayString(FullyQualified)}.{method.Name}({parameters})";
        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

        return new SymbolInfo(
            SymbolId: fqn,
            Kind: method.MethodKind == MethodKind.Constructor ? SymbolKind.Constructor : SymbolKind.Method,
            Name: method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor ? ".ctor" : method.Name,
            FullyQualifiedName: fqn,
            FilePath: file,
            Namespace: method.ContainingType?.ContainingNamespace?.IsGlobalNamespace == false
                ? method.ContainingType.ContainingNamespace.ToDisplayString()
                : null,
            Project: DeriveProject(file),
            Signature: DeclarationHeader(syntax),
            ReturnType: method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Parameters: method.Parameters.Select(p => p.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)).ToList(),
            DeclarationHash: DeclarationHash(syntax));
    }

    private static SymbolInfo? ToSymbol(IPropertySymbol property, string? file)
    {
        // Same Roslyn quirk as methods: qualify member names manually.
        var fqn = $"{property.ContainingType.ToDisplayString(FullyQualified)}.{property.Name}";
        return new SymbolInfo(
            SymbolId: fqn,
            Kind: SymbolKind.Property,
            Name: property.Name,
            FullyQualifiedName: fqn,
            FilePath: file,
            Namespace: property.ContainingType?.ContainingNamespace?.IsGlobalNamespace == false
                ? property.ContainingType.ContainingNamespace.ToDisplayString()
                : null,
            Project: DeriveProject(file),
            Signature: null,
            ReturnType: property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Parameters: Array.Empty<string>());
    }

    private static SymbolInfo? ToSymbol(IFieldSymbol field, string? file)
    {
        var fqn = $"{field.ContainingType.ToDisplayString(FullyQualified)}.{field.Name}";
        return new SymbolInfo(
            SymbolId: fqn,
            Kind: SymbolKind.Field,
            Name: field.Name,
            FullyQualifiedName: fqn,
            FilePath: file,
            Namespace: field.ContainingType?.ContainingNamespace?.IsGlobalNamespace == false
                ? field.ContainingType.ContainingNamespace.ToDisplayString()
                : null,
            Project: DeriveProject(file),
            Signature: null,
            ReturnType: field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Parameters: Array.Empty<string>());
    }

    private static void CollectCallEdges(
        IMethodSymbol method,
        IMethodSymbol methodSymbol,
        string? file,
        IReadOnlySet<string> repoPaths,
        SemanticModel model,
        HashSet<DependencyEdge> edges)
    {
        var syntaxNode = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntaxNode is null)
        {
            return;
        }

        foreach (var invocation in syntaxNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol callee
                || !IsRepoDeclared(callee, repoPaths)
                || callee.Equals(methodSymbol, SymbolEqualityComparer.Default))
            {
                continue;
            }

            edges.Add(new DependencyEdge(
                MethodFqn(methodSymbol),
                MethodFqn(callee),
                EdgeType.Calls,
                file));
        }
    }

    private static void CollectTypeReferenceEdges(
        IMethodSymbol method,
        string containerFqn,
        string? file,
        IReadOnlySet<string> repoPaths,
        SemanticModel model,
        HashSet<DependencyEdge> edges)
    {
        // Object creation inside the method body → REFERENCES_TYPE.
        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is not null)
        {
            foreach (var creation in syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (model.GetTypeInfo(creation).Type is INamedTypeSymbol created
                    && IsRepoDeclared(created, repoPaths))
                {
                    edges.Add(new DependencyEdge(
                        MethodFqn(method),
                        created.ToDisplayString(FullyQualified),
                        EdgeType.ReferencesType,
                        file));
                }
            }
        }

        // Generic type arguments in the body (e.g. AddSingleton<TokenService>(),
        // List<PaymentDto>) → REFERENCES_TYPE from the containing method.
        if (syntax is not null)
        {
            foreach (var generic in syntax.DescendantNodes().OfType<GenericNameSyntax>())
            {
                foreach (var arg in generic.TypeArgumentList.Arguments)
                {
                    if (model.GetTypeInfo(arg).Type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Interface } typeArg
                        && IsRepoDeclared(typeArg, repoPaths))
                    {
                        edges.Add(new DependencyEdge(
                            MethodFqn(method),
                            typeArg.ToDisplayString(FullyQualified),
                            EdgeType.ReferencesType,
                            file));
                    }
                }
            }
        }

        // Return type + parameter types of the method → REFERENCES_TYPE (via its container).
        foreach (var referenced in method.Parameters.Select(p => p.Type).Append(method.ReturnType))
        {
            if (referenced is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Interface }
                && IsRepoDeclared(referenced, repoPaths))
            {
                edges.Add(new DependencyEdge(
                    containerFqn,
                    referenced.ToDisplayString(FullyQualified),
                    EdgeType.ReferencesType,
                    file));
            }
        }
    }

    /// <summary>
    /// Top-level statements (Program.cs): object creations and generic type arguments
    /// that are not wrapped in a method declaration are attributed to the containing
    /// type (the implicit entry point). This captures DI registrations like
    /// <c>AddSingleton&lt;TokenService&gt;()</c> as dependency edges.
    /// </summary>
    private static void CollectTopLevelTypeReferences(
        INamedTypeSymbol type,
        string? file,
        IReadOnlySet<string> repoPaths,
        SemanticModel model,
        HashSet<DependencyEdge> edges)
    {
        var globals = model.SyntaxTree.GetRoot().DescendantNodes().OfType<GlobalStatementSyntax>().ToList();
        if (globals.Count == 0)
        {
            return;
        }

        var from = type.ToDisplayString(FullyQualified);
        foreach (var stmt in globals)
        {
            foreach (var creation in stmt.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (model.GetTypeInfo(creation).Type is INamedTypeSymbol created
                    && IsRepoDeclared(created, repoPaths))
                {
                    edges.Add(new DependencyEdge(
                        from,
                        created.ToDisplayString(FullyQualified),
                        EdgeType.ReferencesType,
                        file));
                }
            }

            foreach (var generic in stmt.DescendantNodes().OfType<GenericNameSyntax>())
            {
                foreach (var arg in generic.TypeArgumentList.Arguments)
                {
                    if (model.GetTypeInfo(arg).Type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Interface } t
                        && IsRepoDeclared(t, repoPaths))
                    {
                        edges.Add(new DependencyEdge(
                            from,
                            t.ToDisplayString(FullyQualified),
                            EdgeType.ReferencesType,
                            file));
                    }
                }
            }
        }
    }

    private static bool IsRepoDeclared(ISymbol symbol, IReadOnlySet<string> repoPaths)
    {
        return symbol.DeclaringSyntaxReferences.Any(r => repoPaths.Contains(r.SyntaxTree.FilePath));
    }

    /// <summary>Unique id for a method: typeFqn.name(paramTypeFqns) — must match ToSymbol(IMethodSymbol).</summary>
    private static string MethodFqn(IMethodSymbol method)
        => $"{method.ContainingType.ToDisplayString(FullyQualified)}.{method.Name}("
           + $"{string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(FullyQualified)))}"
           + ")";

    /// <summary>sha256 of the full declaration text — detects body-only changes.</summary>
    internal static string? DeclarationHash(SyntaxNode? node)
    {
        if (node is null)
        {
            return null;
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(node.ToFullString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>All declaration lines before the body brace — attributes + signature.</summary>
    internal static string? DeclarationHeader(SyntaxNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var lines = node.ToString().Replace("\r\n", "\n").Split('\n');
        var header = new List<string>();
        foreach (var line in lines)
        {
            if (header.Count > 0 && line.Contains('{'))
            {
                break;
            }

            header.Add(line.Trim());
            if (header.Count >= 14)
            {
                break;
            }
        }

        return string.Join("\n", header.Where(l => l.Length > 0));
    }

    /// <summary>Project from the demo layout: "src/AcmePay.Application/…" → "AcmePay.Application".</summary>
    internal static string? DeriveProject(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var segments = filePath.Replace('\\', '/').Split('/');
        if (segments.Length >= 3 && segments[0] == "src")
        {
            return segments[1];
        }

        return segments.Length >= 2 ? segments[0] : null;
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        // Best-effort: force-load likely assemblies, then take whatever the host has
        // loaded plus the runtime directory core assemblies. Implementation assemblies
        // are sufficient for semantic-model queries; unresolved types become warnings.
        foreach (var name in new[]
                 {
                     "System.Runtime", "System.Private.CoreLib", "System.Linq",
                     "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.Configuration.Abstractions",
                     "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.EntityFrameworkCore",
                     "System.Net.Http", "System.Text.Json"
                 })
        {
            try
            {
                Assembly.Load(name);
            }
            catch
            {
                // optional — the runtime dir fallback covers core BCL
            }
        }

        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (string.IsNullOrEmpty(assembly.Location))
                {
                    continue;
                }

                var name = assembly.GetName().Name;
                if (name is not null && seen.Add(name))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            catch
            {
                // skip assemblies without a loadable location
            }
        }

        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (coreDir is not null)
        {
            foreach (var name in new[]
                     {
                         "System.Runtime", "System.Private.CoreLib", "System.Collections", "System.Linq",
                         "System.Console", "System.Net.Http", "System.Net.Primitives", "System.Text.Json",
                         "System.Threading.Tasks", "System.Threading", "System.ComponentModel",
                         "System.Collections.Concurrent", "System.Linq.Expressions", "System.Memory",
                         "System.Private.Uri", "System.Runtime.Extensions", "System.Runtime.Numerics",
                         "System.Text.Encoding.Extensions", "System.IO", "System.IO.FileSystem",
                         "netstandard"
                     })
            {
                var path = Path.Combine(coreDir, name + ".dll");
                if (File.Exists(path) && seen.Add(name))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        return references.ToImmutableArray();
    }
}
