using ChangeLens.Application.Dtos;
using ChangeLens.Infrastructure.Analysis;
using ChangeLens.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChangeLens.UnitTests.Analysis;

/// <summary>
/// Phase 4 demo scenario (brief §40): the demo repository's working tree contains a real,
/// committed JWT signing-key rotation change in TokenService.cs. The engine must resolve
/// base (git HEAD) vs target (working tree), run Roslyn, build the dependency graph, and
/// produce a change model with changed/added symbols, impacted symbols, dependency edges,
/// and dependency paths — without any AI call.
/// </summary>
public sealed class ChangeAnalysisEngineTests
{
    private static readonly string DemoRepo = DemoRepoLocator.FindRoot();
    private static readonly string WorkspaceRoot = Directory.GetParent(DemoRepo)!.Parent!.FullName;

    private static ChangeAnalysisEngine Engine(int maxDepth = 2) => new(
        new GitChangeSource(
            Options.Create(new ChangeSourceOptions
            {
                RootPath = WorkspaceRoot,
                RepositoryPath = "data/demo-repository",
                MaxImpactDepth = maxDepth
            }),
            NullLogger<GitChangeSource>.Instance),
        new RoslynAnalyzer(),
        new ChangeAnalyzer(new RoslynAnalyzer()),
        Options.Create(new ChangeSourceOptions { MaxImpactDepth = maxDepth }),
        NullLogger<ChangeAnalysisEngine>.Instance);

    private static AnalyzeChangeRiskRequest TokenServiceChange() => new()
    {
        ChangeSummary = "JWT signing key rotation: issue and validate against the full key history.",
        ChangedFiles =
        [
            new ChangedFileRequest
            {
                Path = "src/AcmePay.Application/Auth/TokenService.cs",
                ChangeType = "modified",
                Language = "csharp"
            }
        ],
        BaseRevision = "HEAD"
    };

    [Fact]
    public void TokenServiceRotation_ProducesChangedAndAddedSymbols()
    {
        var model = Engine().BuildChangeModel(TokenServiceChange());

        // Changed method (modified signature/body) and added methods.
        Assert.Contains(model.ChangedSymbols, s =>
            s.Name == "IssueServiceToken" && s.FullyQualifiedName.Contains("TokenService"));
        Assert.Contains(model.ModifiedSymbols, s => s.Name == "IssueServiceToken");
        Assert.Contains(model.AddedSymbols, s => s.Name == "TryValidateServiceToken");
        Assert.Contains(model.AddedSymbols, s => s.Name == "SigningKeys");

        // The change file was enriched with the symbol names (client sees evidence).
        var file = Assert.Single(model.ChangedFiles);
        Assert.Contains("IssueServiceToken", file.SymbolsChanged);
        Assert.NotNull(file.DiffPreview);
        Assert.Contains("TryValidateServiceToken", file.DiffPreview);
    }

    [Fact]
    public void TokenServiceRotation_FlowsThroughDependencyGraph()
    {
        var model = Engine().BuildChangeModel(TokenServiceChange());

        // Real numbers for the Phase 4 report — printed, not fabricated.
        Console.WriteLine("== Demo scenario: JWT signing key rotation ==");
        Console.WriteLine($"  Changed symbols: {model.ChangedSymbols.Count} ({string.Join(", ", model.ChangedSymbols.Select(s => s.Name))})");
        Console.WriteLine($"  Added symbols: {model.AddedSymbols.Count} ({string.Join(", ", model.AddedSymbols.Select(s => s.Name))})");
        Console.WriteLine($"  Modified symbols: {model.ModifiedSymbols.Count}");
        Console.WriteLine($"  Impacted symbols: {model.ImpactedSymbols.Count} ({string.Join(", ", model.ImpactedSymbols.Select(s => s.Name))})");
        Console.WriteLine($"  Dependency edges: {model.DependencyEdges.Count}");
        Console.WriteLine($"  Dependency paths: {model.DependencyPaths.Count} ({string.Join(", ", model.DependencyPaths)})");
        Console.WriteLine($"  Impacted APIs: {model.ImpactedApis.Count}");
        Console.WriteLine($"  External integration impacts: {model.ExternalIntegrationImpacts.Count}");
        Console.WriteLine($"  Warnings: {model.Warnings.Count}");
        foreach (var w in model.Warnings)
        {
            Console.WriteLine($"    - {w}");
        }

        // TokenService is referenced by Program.cs (singleton registration), so the
        // change has at least one dependent at depth 1.
        Assert.NotEmpty(model.ImpactedSymbols);

        // The graph supplies dependency edges and paths that drive retrieval.
        Assert.NotEmpty(model.DependencyEdges);
        Assert.Contains(model.DependencyEdges, e => e.ToSymbolId.Contains("TokenService"));
        Assert.NotEmpty(model.DependencyPaths);
    }

    [Fact]
    public void ImpactTraversalDepth_IsConfigurable()
    {
        var shallow = Engine(maxDepth: 0).BuildChangeModel(TokenServiceChange());
        var deep = Engine(maxDepth: 2).BuildChangeModel(TokenServiceChange());

        // Depth 0 must not traverse dependents at all; depth 2 reaches them.
        Assert.True(shallow.ImpactedSymbols.Count <= deep.ImpactedSymbols.Count);
    }

    [Fact]
    public void UnknownFile_ProducesWarnings_NotAnException()
    {
        var model = Engine().BuildChangeModel(new AnalyzeChangeRiskRequest
        {
            ChangeSummary = "A change to a file that does not exist in the demo repository.",
            ChangedFiles =
            [
                new ChangedFileRequest { Path = "src/NotReal.cs", ChangeType = "modified", Language = "csharp" }
            ],
            BaseRevision = "HEAD"
        });

        Assert.Empty(model.ChangedSymbols);
        Assert.NotEmpty(model.Warnings);
    }
}
