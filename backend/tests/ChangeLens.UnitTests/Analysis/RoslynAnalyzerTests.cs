using ChangeLens.Infrastructure.Analysis;

namespace ChangeLens.UnitTests.Analysis;

/// <summary>Deterministic in-memory fixtures (brief §31) — no disk, no demo repo.</summary>
public sealed class RoslynAnalyzerTests
{
    private static readonly RoslynAnalyzer Analyzer = new();

    private static RepositoryAnalysis Analyze(params (string Path, string Content)[] files)
        => Analyzer.Analyze(files.Select(f => new SourceFile(f.Path, f.Content)).ToList());

    // --- symbol extraction ---

    [Fact]
    public void ExtractsClassesMethodsProperties()
    {
        var analysis = Analyze((
            "src/Demo/PaymentService.cs",
            """
            namespace Demo;

            public sealed class PaymentService
            {
                public string Status { get; set; } = "pending";

                public void Charge(decimal amount) { }
            }
            """));

        var service = Assert.Single(analysis.Graph.Nodes.Values, s => s.Name == "PaymentService");
        Assert.Equal(SymbolKind.Class, service.Kind);
        Assert.Equal("Demo", service.Namespace);
        Assert.Equal("Demo", service.Project);

        var charge = Assert.Single(analysis.Graph.Nodes.Values, s => s.Name == "Charge");
        Assert.Equal(SymbolKind.Method, charge.Kind);
        Assert.Contains("decimal", string.Join(' ', charge.Parameters));
        Assert.Equal("void", charge.ReturnType);

        var status = Assert.Single(analysis.Graph.Nodes.Values, s => s.Name == "Status");
        Assert.Equal(SymbolKind.Property, status.Kind);
        Assert.Equal("string", status.ReturnType);
    }

    [Fact]
    public void ExtractsInterfaces()
    {
        var analysis = Analyze((
            "src/Demo/IRepository.cs",
            "namespace Demo; public interface IRepository { Task SaveAsync(); }"));

        var iface = Assert.Single(analysis.Graph.Nodes.Values, s => s.Name == "IRepository");
        Assert.Equal(SymbolKind.Interface, iface.Kind);
    }

    [Fact]
    public void ExtractsConstructorsAndFields()
    {
        var analysis = Analyze((
            "src/Demo/Worker.cs",
            """
            namespace Demo;
            public sealed class Worker
            {
                private readonly PaymentService _service;

                public Worker(PaymentService service) => _service = service;
            }
            """));

        Assert.Contains(analysis.Graph.Nodes.Values, s => s.Kind == SymbolKind.Constructor && s.Name == ".ctor");
        Assert.Contains(analysis.Graph.Nodes.Values, s => s.Kind == SymbolKind.Field && s.Name == "_service");
    }

    // --- dependency detection ---

    [Fact]
    public void DetectsMethodCalls()
    {
        var analysis = Analyze(
            ("src/Demo/PaymentService.cs", "namespace Demo; public sealed class PaymentService { public void Charge() { } }"),
            ("src/Demo/Controller.cs",
             """
             namespace Demo;
             public sealed class Controller
             {
                 private readonly PaymentService _svc;
                 public Controller(PaymentService svc) => _svc = svc;
                 public void Run() => _svc.Charge();
             }
             """));

        var controller = analysis.Graph.Nodes.Values.Single(s => s.Name == "Controller");
        var paymentService = analysis.Graph.Nodes.Values.Single(s => s.Name == "PaymentService");
        var charge = analysis.Graph.Nodes.Values.Single(s => s.Name == "Charge");

        Assert.Contains(analysis.Graph.Edges,
            e => e.FromSymbolId == controller.SymbolId && e.ToSymbolId == paymentService.SymbolId && e.Type == EdgeType.ReferencesType);
        Assert.Contains(analysis.Graph.Edges,
            e => e.Type == EdgeType.Calls && e.ToSymbolId == charge.SymbolId);
    }

    [Fact]
    public void DetectsInheritanceAndInterfaceImplementation()
    {
        var analysis = Analyze(
            ("src/Demo/Base.cs", "namespace Demo; public abstract class BaseHandler { }"),
            ("src/Demo/IFoo.cs", "namespace Demo; public interface IFoo { void Run(); }"),
            ("src/Demo/Concrete.cs", "namespace Demo; public sealed class Concrete : BaseHandler, IFoo { public void Run() { } }"));

        var concrete = analysis.Graph.Nodes.Values.Single(s => s.Name == "Concrete");
        var baseHandler = analysis.Graph.Nodes.Values.Single(s => s.Name == "BaseHandler");
        var foo = analysis.Graph.Nodes.Values.Single(s => s.Name == "IFoo");

        Assert.Contains(analysis.Graph.Edges, e => e.FromSymbolId == concrete.SymbolId && e.ToSymbolId == baseHandler.SymbolId && e.Type == EdgeType.Inherits);
        Assert.Contains(analysis.Graph.Edges, e => e.FromSymbolId == concrete.SymbolId && e.ToSymbolId == foo.SymbolId && e.Type == EdgeType.Implements);
    }

    [Fact]
    public void DetectsObjectCreationReferences()
    {
        var analysis = Analyze(
            ("src/Demo/Target.cs", "namespace Demo; public sealed class Target { public Target() { } }"),
            ("src/Demo/Factory.cs",
             "namespace Demo; public sealed class Factory { public Target Make() => new Target(); }"));

        var factory = analysis.Graph.Nodes.Values.Single(s => s.Name == "Factory");
        var target = analysis.Graph.Nodes.Values.Single(s => s.Name == "Target");

        Assert.Contains(analysis.Graph.Edges,
            e => e.FromSymbolId == factory.SymbolId && e.ToSymbolId == target.SymbolId && e.Type == EdgeType.ReferencesType);
    }

    [Fact]
    public void PrimaryConstructorParametersCreateTypeEdges()
    {
        var analysis = Analyze(
            ("src/Demo/Gateway.cs", "namespace Demo; public sealed class Gateway { }"),
            ("src/Demo/Client.cs",
             "namespace Demo; public sealed class Client(Gateway gateway) { public void Run() { } }"));

        var client = analysis.Graph.Nodes.Values.Single(s => s.Name == "Client");
        var gateway = analysis.Graph.Nodes.Values.Single(s => s.Name == "Gateway");

        Assert.Contains(analysis.Graph.Edges,
            e => e.FromSymbolId == client.SymbolId && e.ToSymbolId == gateway.SymbolId && e.Type == EdgeType.ReferencesType);
    }

    // --- graph ---

    [Fact]
    public void GraphTraversalFindsDependentsAtDepth()
    {
        // A → B → C  (B depends on C; A depends on B)
        var analysis = Analyze(
            ("src/Demo/C.cs", "namespace Demo; public sealed class C { public void Run() { } }"),
            ("src/Demo/B.cs", "namespace Demo; public sealed class B { private readonly C _c; public B(C c) => _c = c; public void Run() => _c.Run(); }"),
            ("src/Demo/A.cs", "namespace Demo; public sealed class A { private readonly B _b; public A(B b) => _b = b; public void Run() => _b.Run(); }"));

        var graph = analysis.Graph;
        var c = graph.Nodes.Values.Single(s => s.Name == "C");

        // Direct dependents of C: B only.
        Assert.Equal(["B"], graph.GetDirectDependents(c.SymbolId).Select(s => s.Name).ToArray());

        // Depth-2 traversal from C reaches B then A.
        var related = graph.GetRelatedSymbols(c.SymbolId, depth: 2).Select(s => s.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["A", "B"], related);
    }
}
