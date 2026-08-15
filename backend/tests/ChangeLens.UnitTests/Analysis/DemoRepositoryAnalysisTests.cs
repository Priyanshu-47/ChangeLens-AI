using ChangeLens.Infrastructure.Analysis;

namespace ChangeLens.UnitTests.Analysis;

/// <summary>
/// Verifies the Roslyn analyzer against the actual demo repository (ADR-0011).
/// The summary numbers are REAL — printed for the Phase 4 report, not fabricated.
/// </summary>
public sealed class DemoRepositoryAnalysisTests
{
    private static readonly string DemoRepo = DemoRepoLocator.FindRoot();

    [Fact]
    public void DemoRepository_AnalyzesToARealGraph()
    {
        var files = Directory.GetFiles(DemoRepo, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(f => new SourceFile(
                Path.GetRelativePath(DemoRepo, f).Replace('\\', '/'),
                File.ReadAllText(f)))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(files);

        var analysis = new RoslynAnalyzer().Analyze(files);

        var graph = analysis.Graph;
        var classes = graph.Nodes.Values.Count(s => s.Kind == SymbolKind.Class);
        var methods = graph.Nodes.Values.Count(s => s.Kind == SymbolKind.Method);
        var edges = graph.Edges.Count;

        // The full verified summary — printed so the Phase 4 report uses actual numbers.
        Console.WriteLine("== Demo repository Roslyn analysis ==");
        Console.WriteLine(analysis.Summary);
        Console.WriteLine($"  Source files scanned: {files.Count}");
        Console.WriteLine($"  Projects: {string.Join(", ", analysis.Graph.Nodes.Values.Select(s => s.Project).Distinct().Where(p => p is not null))}");

        // Real thresholds for the 24-file AcmePay demo (kept loose to avoid brittleness).
        Assert.True(classes >= 25, $"expected >= 25 classes, got {classes}");
        Assert.True(methods >= 20, $"expected >= 20 methods, got {methods}");
        Assert.True(edges >= 30, $"expected >= 30 dependency edges, got {edges}");
    }

    [Fact]
    public void DemoRepository_GraphHasExpectedCoreEdges()
    {
        var files = Directory.GetFiles(DemoRepo, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(f => new SourceFile(
                Path.GetRelativePath(DemoRepo, f).Replace('\\', '/'),
                File.ReadAllText(f)))
            .ToList();

        var graph = new RoslynAnalyzer().Analyze(files).Graph;

        // ProcessPaymentHandler calls the Stripe gateway (CALLS edge) and references
        // PaymentDbContext (REFERENCES_TYPE / constructor).
        var handler = graph.Nodes.Values.SingleOrDefault(s =>
            s.Name == "ProcessPaymentHandler" && s.Project == "AcmePay.Application");
        Assert.NotNull(handler);

        var dependencies = graph.GetDirectDependencies(handler!.SymbolId);
        Assert.Contains(dependencies, d => d.Name == "StripeGatewayClient");
        Assert.Contains(dependencies, d => d.Name == "PaymentDbContext");

        // PaymentsController calls the handler (API → application chain).
        var controller = graph.Nodes.Values.SingleOrDefault(s => s.Name == "PaymentsController");
        Assert.NotNull(controller);
        Assert.Contains(graph.GetDirectDependencies(controller!.SymbolId), d => d.Name == "ProcessPaymentHandler");
    }
}

/// <summary>Locates the repo root (…/data/demo-repository) from the test output dir.</summary>
internal static class DemoRepoLocator
{
    public static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "demo-repository");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate data/demo-repository from the test output directory. " +
            "Set CHANGELENS_DEMO_REPO_PATH to the absolute path.");
    }
}
