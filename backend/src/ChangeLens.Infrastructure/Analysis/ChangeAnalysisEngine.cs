using ChangeLens.Application.Dtos;
using ChangeLens.Application.Ports;
using ChangeLens.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChangeLens.Infrastructure.Analysis;

/// <summary>
/// Change-intelligence engine (ADR-0011): resolves the change via the safe git source,
/// builds the target repository state, runs Roslyn change analysis (symbols, graph,
/// impact, APIs, external integrations), and maps it to the normalized change model the
/// AI request is enriched with. Analyzes only — never executes repository code.
/// </summary>
public sealed class ChangeAnalysisEngine(
    IChangeSource changeSource,
    RoslynAnalyzer analyzer,
    ChangeAnalyzer changeAnalyzer,
    IOptions<ChangeSourceOptions> options,
    ILogger<ChangeAnalysisEngine> logger) : IChangeAnalysisEngine
{
    public ChangeModelDto BuildChangeModel(AnalyzeChangeRiskRequest request)
    {
        var resolution = changeSource.ResolveChange(
            request.ChangedFiles, request.BaseRevision, request.TargetRevision);

        var targetFiles = ReadTargetState(resolution.RepositoryPath, resolution.Files);

        var changedFiles = resolution.Files
            .Select(f => new ChangedFile(f.Path, f.ChangeType, f.BaseContent, f.TargetContent))
            .ToList();

        var result = changeAnalyzer.Analyze(
            targetFiles, targetFiles, changedFiles, options.Value.MaxImpactDepth);

        var changedSymbols = result.ChangedSymbols.Select(ToSymbolDto).ToList();
        var impactedSymbols = result.ImpactedSymbols.Select(ToSymbolDto).ToList();

        logger.LogInformation(
            "Change analysis: files {FileCount}, changed symbols {ChangedCount}, " +
            "impacted symbols {ImpactedCount}, edges {EdgeCount}, warnings {WarningCount}",
            resolution.Files.Count, changedSymbols.Count, impactedSymbols.Count,
            result.RelevantEdges.Count, result.Warnings.Count);

        return new ChangeModelDto
        {
            ChangedFiles = EnrichFiles(request.ChangedFiles, resolution.Files, result.ChangedSymbols),
            ChangedSymbols = changedSymbols,
            AddedSymbols = result.AddedSymbols.Select(ToSymbolDto).ToList(),
            RemovedSymbols = result.RemovedSymbols.Select(ToSymbolDto).ToList(),
            ModifiedSymbols = result.ModifiedSymbols.Select(ToSymbolDto).ToList(),
            ImpactedSymbols = impactedSymbols,
            DependencyEdges = result.RelevantEdges.Select(ToEdgeDto).ToList(),
            ImpactedApis = result.ImpactedApis.Select(a => new ApiEndpointDto
            {
                Controller = a.Controller,
                Route = a.Route,
                HttpMethod = a.HttpMethod,
                Action = a.Action,
                RequestDto = a.RequestDto,
                ResponseDto = a.ResponseDto,
                FilePath = a.FilePath
            }).ToList(),
            ExternalIntegrationImpacts = result.ExternalIntegrationImpacts.Select(i =>
                new ExternalIntegrationImpactDto
                {
                    ClientType = i.ClientType,
                    FilePath = i.FilePath,
                    EndpointHints = i.EndpointHints.ToList(),
                    RetryHint = i.RetryHint,
                    TimeoutHint = i.TimeoutHint,
                    ConnectedChangedSymbols = i.ConnectedChangedSymbols.ToList()
                }).ToList(),
            ImpactedServices = result.ImpactedServices.ToList(),
            DependencyPaths = result.DependencyPaths.ToList(),
            Warnings = resolution.Warnings.Concat(result.Warnings).Distinct(StringComparer.Ordinal).ToList()
        };
    }

    public SymbolDependencyPathsDto FindDependencyPaths(string symbol, int maxDepth)
    {
        // The repository is server configuration, not user input (GitChangeSource
        // validates it inside the allowed root). Traversal is bounded: the tool caps
        // maxDepth, and the engine re-caps defensively against config drift.
        var resolution = changeSource.ResolveChange(Array.Empty<ChangedFileRequest>(), null, null);
        var targetFiles = ReadTargetState(resolution.RepositoryPath, resolution.Files);
        var analysis = analyzer.Analyze(targetFiles);
        var graph = analysis.Graph;

        var resolvedId = ResolveSymbolId(graph, symbol);
        if (resolvedId is null)
        {
            return new SymbolDependencyPathsDto
            {
                Warnings = [$"No symbol matching '{symbol}' was found in the repository graph."]
            };
        }

        var depth = Math.Clamp(maxDepth, 1, 4);
        var seen = new HashSet<string>(StringComparer.Ordinal) { resolvedId };
        var reachable = new HashSet<string>(StringComparer.Ordinal) { resolvedId };
        var frontier = new List<string> { resolvedId };
        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var next = new List<string>();
            foreach (var current in frontier)
            {
                foreach (var dependent in graph.GetDirectDependents(current))
                {
                    if (seen.Add(dependent.SymbolId))
                    {
                        reachable.Add(dependent.SymbolId);
                        next.Add(dependent.SymbolId);
                    }
                }

                foreach (var dependency in graph.GetDirectDependencies(current))
                {
                    if (seen.Add(dependency.SymbolId))
                    {
                        reachable.Add(dependency.SymbolId);
                        next.Add(dependency.SymbolId);
                    }
                }
            }

            frontier = next;
        }

        var paths = graph.Edges
            .Where(e => reachable.Contains(e.FromSymbolId) && reachable.Contains(e.ToSymbolId))
            .OrderBy(e => e.FromSymbolId, StringComparer.Ordinal)
            .ThenBy(e => e.ToSymbolId, StringComparer.Ordinal)
            .Select(e => new SymbolDependencyPathDto
            {
                From = e.FromSymbolId,
                To = e.ToSymbolId,
                EdgeType = e.Type.ToString().ToUpperInvariant(),
                FilePath = e.FilePath
            })
            .ToList();

        return new SymbolDependencyPathsDto
        {
            ResolvedSymbol = resolvedId,
            Paths = paths,
            Warnings = analysis.Warnings.Take(5).ToList()
        };
    }

    /// <summary>Resolve a tool-supplied symbol: exact id, exact name, or unique name suffix.</summary>
    private static string? ResolveSymbolId(DependencyGraph graph, string symbol)
    {
        if (graph.Nodes.ContainsKey(symbol))
        {
            return symbol;
        }

        var candidates = graph.Nodes.Values
            .Where(s => s.Name.Equals(symbol, StringComparison.Ordinal)
                        || s.SymbolId.EndsWith("." + symbol, StringComparison.Ordinal)
                        || s.SymbolId.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.SymbolId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private List<SourceFile> ReadTargetState(string repoDir, IReadOnlyList<ResolvedChangeFile> resolved)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        if (Directory.Exists(repoDir))
        {
            foreach (var path in Directory.GetFiles(repoDir, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = Path.GetRelativePath(repoDir, path).Replace('\\', '/');
                if (normalized.StartsWith("obj/", StringComparison.Ordinal)
                    || normalized.StartsWith("bin/", StringComparison.Ordinal))
                {
                    continue;
                }

                files[normalized] = File.ReadAllText(path);
            }
        }

        // The resolved target content wins over the working tree (e.g. target revision).
        foreach (var file in resolved)
        {
            if (file.TargetContent is not null)
            {
                files[file.Path.Replace('\\', '/')] = file.TargetContent;
            }
        }

        return files
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new SourceFile(kv.Key, kv.Value))
            .ToList();
    }

    private static List<ChangedFileRequest> EnrichFiles(
        IReadOnlyList<ChangedFileRequest> requested,
        IReadOnlyList<ResolvedChangeFile> resolved,
        IReadOnlyList<SymbolInfo> changedSymbols)
    {
        var byPath = resolved.ToDictionary(
            r => r.Path.Replace('\\', '/'), r => r, StringComparer.Ordinal);

        return requested.Select(f =>
        {
            var path = f.Path.Replace('\\', '/');
            var symbols = changedSymbols
                .Where(s => s.FilePath is not null
                            && string.Equals(Normalize(s.FilePath), path, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            byPath.TryGetValue(path, out var resolvedFile);

            return new ChangedFileRequest
            {
                Path = f.Path,
                ChangeType = resolvedFile?.ChangeType ?? f.ChangeType,
                Language = f.Language,
                SymbolsChanged = symbols,
                DiffPreview = BuildDiffPreview(resolvedFile?.BaseContent, resolvedFile?.TargetContent)
            };
        }).ToList();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>Best-effort line preview of base→target (documented as a preview, not a patch).</summary>
    internal static string? BuildDiffPreview(string? baseContent, string? targetContent)
    {
        if (baseContent is null)
        {
            return targetContent is null ? null : Truncate(targetContent);
        }

        if (targetContent is null)
        {
            return Truncate(baseContent);
        }

        if (baseContent == targetContent)
        {
            return null;
        }

        var a = baseContent.Replace("\r\n", "\n").Split('\n');
        var b = targetContent.Replace("\r\n", "\n").Split('\n');
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start])
        {
            start++;
        }

        var aEnd = a.Length;
        var bEnd = b.Length;
        while (aEnd > start && bEnd > start && a[aEnd - 1] == b[bEnd - 1])
        {
            aEnd--;
            bEnd--;
        }

        var lines = new List<string> { $"@@ -{start + 1},{aEnd - start} +{start + 1},{bEnd - start} @@" };
        for (var i = start; i < aEnd; i++)
        {
            lines.Add("-" + a[i]);
        }

        for (var i = start; i < bEnd; i++)
        {
            lines.Add("+" + b[i]);
        }

        return Truncate(string.Join("\n", lines));
    }

    private static string Truncate(string s) => s.Length <= 20_000 ? s : s[..20_000];

    private static ChangedSymbolDto ToSymbolDto(SymbolInfo s) => new()
    {
        SymbolId = s.SymbolId,
        Kind = s.Kind.ToString(),
        Name = s.Name,
        FullyQualifiedName = s.FullyQualifiedName,
        FilePath = s.FilePath,
        Namespace = s.Namespace,
        Project = s.Project,
        Signature = s.Signature,
        ReturnType = s.ReturnType,
        Parameters = s.Parameters.ToList()
    };

    private static DependencyEdgeDto ToEdgeDto(DependencyEdge e) => new()
    {
        FromSymbolId = e.FromSymbolId,
        ToSymbolId = e.ToSymbolId,
        EdgeType = e.Type.ToString().ToUpperInvariant(),
        FilePath = e.FilePath
    };
}
