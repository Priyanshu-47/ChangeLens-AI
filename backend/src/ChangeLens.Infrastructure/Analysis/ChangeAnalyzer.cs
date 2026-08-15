using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ChangeLens.Infrastructure.Analysis;

/// <summary>One changed file: path + optional base/target content (at least one present).</summary>
public sealed record ChangedFile(
    string Path,
    string ChangeType,
    string? BaseContent = null,
    string? TargetContent = null);

/// <summary>A potentially affected API endpoint with the changed symbols that reach it.</summary>
public sealed record ApiEndpoint(
    string Controller,
    string Route,
    string HttpMethod,
    string Action,
    string? RequestDto,
    string? ResponseDto,
    string FilePath);

/// <summary>An external integration (HttpClient-based) connected to the change.</summary>
public sealed record ExternalIntegrationImpact(
    string ClientType,
    string FilePath,
    IReadOnlyList<string> EndpointHints,
    string? RetryHint,
    string? TimeoutHint,
    IReadOnlyList<string> ConnectedChangedSymbols);

/// <summary>Result of a symbol-level change analysis (docs/rag-architecture.md §9–18).</summary>
public sealed record ChangeAnalysisResult(
    IReadOnlyList<SymbolInfo> ChangedSymbols,
    IReadOnlyList<SymbolInfo> AddedSymbols,
    IReadOnlyList<SymbolInfo> RemovedSymbols,
    IReadOnlyList<SymbolInfo> ModifiedSymbols,
    IReadOnlyList<SymbolInfo> ImpactedSymbols,
    IReadOnlyList<DependencyEdge> RelevantEdges,
    IReadOnlyList<ApiEndpoint> ImpactedApis,
    IReadOnlyList<ExternalIntegrationImpact> ExternalIntegrationImpacts,
    IReadOnlyList<string> ImpactedServices,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<string> DependencyPaths
        => ChangedSymbols
            .Select(s => s.FilePath)
            .Concat(ImpactedSymbols.Select(s => s.FilePath))
            .Where(p => p is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList()!;
}

/// <summary>
/// Symbol-level change analysis: diffs base vs target symbol sets, then walks the
/// target dependency graph (configurable depth) to find impacted symbols, APIs, and
/// external integrations. Deterministic and evidence-traceable (ADR-0011, brief §15–18).
/// </summary>
public sealed class ChangeAnalyzer(RoslynAnalyzer analyzer)
{
    public const int DefaultMaxImpactDepth = 2;

    /// <summary>Analyze a change given base + target repository states.</summary>
    public ChangeAnalysisResult Analyze(
        IReadOnlyList<SourceFile> baseFiles,
        IReadOnlyList<SourceFile> targetFiles,
        IReadOnlyList<ChangedFile> changedFiles,
        int maxImpactDepth = DefaultMaxImpactDepth)
    {
        var warnings = new List<string>();

        // Symbol-level diff is computed from the per-file base/target contents carried by
        // the ChangedFile records (deterministic per-file diff, no Git needed for the demo
        // path). Added files carry TargetContent only; deleted files BaseContent only.
        var baseSymbols = SymbolSetForFiles(changedFiles
            .Where(c => c.BaseContent is not null)
            .Select(c => new SourceFile(c.Path, c.BaseContent!))
            .ToList());
        var targetSymbols = SymbolSetForFiles(changedFiles
            .Where(c => c.TargetContent is not null)
            .Select(c => new SourceFile(c.Path, c.TargetContent!))
            .ToList());

        var added = targetSymbols.Keys.Except(baseSymbols.Keys, StringComparer.Ordinal)
            .Select(id => targetSymbols[id]).ToList();
        var removed = baseSymbols.Keys.Except(targetSymbols.Keys, StringComparer.Ordinal)
            .Select(id => baseSymbols[id]).ToList();
        var common = targetSymbols.Keys.Intersect(baseSymbols.Keys, StringComparer.Ordinal).ToList();
        // Symbols that exist in both but whose declaration text changed.
        var modified = common
            .Where(id => !SymbolEquals(baseSymbols[id], targetSymbols[id]))
            .Select(id => targetSymbols[id]).ToList();

        // A signature change alters the SymbolId (parameters are part of the id), so the
        // same member renders as removed+added. Reclassify same-name/same-kind/same-type
        // pairs as modified (target version) — the change that matters is the member edit.
        var reclassified = new List<SymbolInfo>();
        foreach (var candidate in added.ToList())
        {
            if (candidate.Kind is not (SymbolKind.Method or SymbolKind.Constructor
                or SymbolKind.Property or SymbolKind.Field))
            {
                continue;
            }

            var match = removed.FirstOrDefault(r =>
                r.Kind == candidate.Kind
                && r.Name == candidate.Name
                && DeclaringTypeId(r) == DeclaringTypeId(candidate));
            if (match is not null)
            {
                added.Remove(candidate);
                removed.Remove(match);
                reclassified.Add(candidate);
            }
        }

        modified = modified.Concat(reclassified).ToList();

        var changed = added.Concat(removed).Concat(modified).DistinctBy(s => s.SymbolId).ToList();

        warnings.AddRange(changedFiles
            .Where(c => c.BaseContent is null && c.TargetContent is null)
            .Select(c => $"Changed file {c.Path} has neither base nor target content; it was ignored."));

        var targetAnalysis = analyzer.Analyze(targetFiles);
        var graph = targetAnalysis.Graph;
        warnings.AddRange(targetAnalysis.Warnings);

        // Impact traversal: dependents of the changed symbols, up to maxImpactDepth.
        var impacted = new List<SymbolInfo>();
        var seen = new HashSet<string>(changed.Select(s => s.SymbolId), StringComparer.Ordinal);
        var frontier = changed.Select(s => s.SymbolId).ToList();

        for (var level = 0; level < maxImpactDepth && frontier.Count > 0; level++)
        {
            var next = new List<string>();
            foreach (var id in frontier)
            {
                foreach (var dependent in graph.GetDirectDependents(id))
                {
                    if (seen.Add(dependent.SymbolId))
                    {
                        impacted.Add(dependent);
                        next.Add(dependent.SymbolId);
                    }
                }
            }

            frontier = next;
        }

        // Impacted APIs: controller actions whose transitive call-graph reaches a changed symbol.
        var apiEndpoints = ExtractApiEndpoints(targetAnalysis);
        var impactedApis = apiEndpoints
            .Where(e => ReachableChangedSymbol(e.ActionMethodId, graph, changed))
            .Select(e => e.Endpoint)
            .ToList();

        // External integration impact: changed/impacted symbols connected to HttpClient-based
        // clients, either as client members or via a dependency edge (the changed symbol calls
        // or references the client). Only connected integrations are surfaced.
        var integrations = ExtractExternalIntegrations(targetAnalysis, targetFiles, changed.Concat(impacted).ToList());
        var externalImpacts = integrations.Values.ToList();

        var impactedServices = changed.Concat(impacted)
            .Select(s => s.Project)
            .Where(p => p is not null)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList()!;

        var relevantIds = changed.Concat(impacted).Select(s => s.SymbolId).ToHashSet(StringComparer.Ordinal);

        return new ChangeAnalysisResult(
            ChangedSymbols: changed,
            AddedSymbols: added,
            RemovedSymbols: removed,
            ModifiedSymbols: modified,
            ImpactedSymbols: impacted,
            RelevantEdges: graph.EdgesTouching(relevantIds),
            ImpactedApis: impactedApis,
            ExternalIntegrationImpacts: externalImpacts,
            ImpactedServices: impactedServices,
            Warnings: warnings);
    }

    /// <summary>Symbols declared in the given files (used to diff base vs target versions).</summary>
    private Dictionary<string, SymbolInfo> SymbolSetForFiles(IReadOnlyList<SourceFile> files)
    {
        var analysis = analyzer.Analyze(files);
        return analysis.Graph.Nodes.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>Containing-type id of a member: strips the trailing ".name" / ".name(params)".</summary>
    private static string DeclaringTypeId(SymbolInfo member)
    {
        var fqn = member.FullyQualifiedName;
        var paren = fqn.IndexOf('(');
        var namePart = paren >= 0 ? fqn[..paren] : fqn;
        var dot = namePart.LastIndexOf('.');
        return dot > 0 ? namePart[..dot] : fqn;
    }

    private static bool SymbolEquals(SymbolInfo a, SymbolInfo b)
        => a.SymbolId == b.SymbolId
           && a.DeclarationHash == b.DeclarationHash;

    private static bool ReachableChangedSymbol(
        string? actionMethodId, DependencyGraph graph, IReadOnlyList<SymbolInfo> changed)
    {
        if (actionMethodId is null)
        {
            return false;
        }

        var changedIds = changed.Select(s => s.SymbolId).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { actionMethodId };
        var frontier = new List<string> { actionMethodId };

        for (var hop = 0; hop < 6 && frontier.Count > 0; hop++)
        {
            var next = new List<string>();
            foreach (var id in frontier)
            {
                if (changedIds.Contains(id))
                {
                    return true;
                }

                foreach (var dependency in graph.GetDirectDependencies(id))
                {
                    if (seen.Add(dependency.SymbolId))
                    {
                        next.Add(dependency.SymbolId);
                    }
                }
            }

            frontier = next;
        }

        return false;
    }

    /// <summary>Controller actions (controllers = classes deriving from ControllerBase).</summary>
    private static IReadOnlyList<(ApiEndpoint Endpoint, string? ActionMethodId)> ExtractApiEndpoints(
        RepositoryAnalysis analysis)
    {
        var result = new List<(ApiEndpoint, string?)>();
        var controllers = analysis.Graph.Nodes.Values
            .Where(s => s.Kind == SymbolKind.Class && s.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();

        foreach (var controller in controllers)
        {
            var routePrefix = ControllerRoute(controller.Signature) ?? controller.Name.Replace("Controller", "");

            var methodNodes = analysis.Graph.Nodes.Values
                .Where(s => s.Kind == SymbolKind.Method
                            && s.FullyQualifiedName.StartsWith(controller.FullyQualifiedName + ".", StringComparison.Ordinal))
                .ToList();

            foreach (var action in methodNodes)
            {
                var httpMethod = HttpMethodOf(action.Signature);
                if (httpMethod is null)
                {
                    continue; // not a request action
                }

                var route = ResolveRoute(routePrefix, action.Signature);
                result.Add((
                    new ApiEndpoint(
                        controller.Name,
                        route,
                        httpMethod,
                        action.Name,
                        RequestDtoOf(action),
                        ResponseDtoOf(action),
                        controller.FilePath ?? action.FilePath ?? string.Empty),
                    action.SymbolId));
            }
        }

        return result;
    }

    private static string? ControllerRoute(string? signature)
    {
        if (signature is null)
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            signature, @"Route\(\s*""([^""]+)""");
        return match.Success ? match.Groups[1].Value.Trim('/') : null;
    }

    private static string? HttpMethodOf(string? signature)
    {
        if (signature is null)
        {
            return null;
        }

        var verbs = new (string Token, string Method)[]
        {
            ("HttpGet", "GET"), ("HttpPost", "POST"), ("HttpPut", "PUT"),
            ("HttpPatch", "PATCH"), ("HttpDelete", "DELETE")
        };

        foreach (var (token, method) in verbs)
        {
            if (signature.Contains(token, StringComparison.Ordinal))
            {
                return method;
            }
        }

        return null;
    }

    private static string ResolveRoute(string prefix, string? signature)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            segments.Add(prefix.Trim('/'));
        }

        if (signature is not null)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                signature, @"Http(Get|Post|Put|Patch|Delete)\(\s*""([^""]+)""");
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
            {
                segments.Add(match.Groups[2].Value.Trim('/'));
            }
        }

        return "/" + string.Join("/", segments);
    }

    private static string? RequestDtoOf(SymbolInfo action)
    {
        var param = action.Parameters.FirstOrDefault(p => !p.Contains("CancellationToken", StringComparison.Ordinal));
        return param;
    }

    private static string? ResponseDtoOf(SymbolInfo action)
    {
        var returnType = action.ReturnType;
        if (returnType is null)
        {
            return null;
        }

        if (returnType.StartsWith("Task<", StringComparison.Ordinal))
        {
            return returnType[5..^1];
        }

        return returnType;
    }

    /// <summary>HttpClient-based types (external integrations) with their endpoint/timeout/retry hints.</summary>
    private static IReadOnlyDictionary<string, ExternalIntegrationImpact> ExtractExternalIntegrations(
        RepositoryAnalysis analysis, IReadOnlyList<SourceFile> targetFiles, IReadOnlyList<SymbolInfo> connectedSymbols)
    {
        var contentByPath = targetFiles.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);
        var result = new Dictionary<string, ExternalIntegrationImpact>(StringComparer.Ordinal);

        foreach (var type in analysis.Graph.Nodes.Values.Where(s => s.Kind == SymbolKind.Class))
        {
            var members = analysis.Graph.Nodes.Values
                .Where(s => s.FullyQualifiedName.StartsWith(type.FullyQualifiedName + ".", StringComparison.Ordinal))
                .ToList();

            var hasHttpClient = members.Any(m =>
                (m.Kind == SymbolKind.Field || m.Kind == SymbolKind.Property)
                && m.ReturnType?.Contains("HttpClient", StringComparison.Ordinal) == true)
                || type.Signature?.Contains("HttpClient", StringComparison.Ordinal) == true
                || type.Name.Contains("Client", StringComparison.Ordinal)
                   && analysis.Graph.GetDirectDependencies(type.SymbolId)
                       .Any(d => d.Name.Contains("HttpClient", StringComparison.Ordinal));

            if (!hasHttpClient)
            {
                continue;
            }

            // Client-related ids: the client type plus every member we extracted for it.
            var clientIds = members.Select(m => m.SymbolId).Append(type.SymbolId).ToHashSet(StringComparer.Ordinal);

            // Changed/impacted symbols that are members of the client itself.
            var memberConnected = members
                .Where(m => connectedSymbols.Any(s => s.SymbolId == m.SymbolId))
                .Select(m => m.SymbolId)
                .ToList();

            // Changed/impacted symbols with a dependency edge to/from the client or its members.
            var edgeConnected = connectedSymbols
                .Where(s => analysis.Graph.Edges.Any(e =>
                    (e.FromSymbolId == s.SymbolId && clientIds.Contains(e.ToSymbolId))
                    || (e.ToSymbolId == s.SymbolId && clientIds.Contains(e.FromSymbolId))))
                .Select(s => s.SymbolId)
                .ToList();

            var connected = memberConnected.Concat(edgeConnected).Distinct(StringComparer.Ordinal).ToList();
            if (connected.Count == 0)
            {
                continue; // integration exists but is not touched by this change
            }

            var file = type.FilePath;
            // Prefer the in-memory analyzed content (fixtures/tests); fall back to disk for
            // on-disk repositories such as the demo repo.
            var text = file is not null && contentByPath.TryGetValue(file, out var content)
                ? content
                : file is not null && File.Exists(file) ? File.ReadAllText(file) : string.Empty;
            var endpointHints = new List<string>();
            var retryHint = (string?)null;
            var timeoutHint = (string?)null;

            var tree = CSharpSyntaxTree.ParseText(text);
            foreach (var literal in tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>())
            {
                if (literal.Token.Value is string value)
                {
                    if (value.StartsWith('/') || value.StartsWith("v1/", StringComparison.Ordinal))
                    {
                        if (endpointHints.Count < 5)
                        {
                            endpointHints.Add(value);
                        }
                    }

                    if (value.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        timeoutHint ??= value;
                    }

                    if (value.Contains("retr", StringComparison.OrdinalIgnoreCase))
                    {
                        retryHint ??= value;
                    }
                }
            }

            result[type.SymbolId] = new ExternalIntegrationImpact(
                ClientType: type.Name,
                FilePath: file ?? string.Empty,
                EndpointHints: endpointHints,
                RetryHint: retryHint,
                TimeoutHint: timeoutHint,
                ConnectedChangedSymbols: connected);
        }

        return result;
    }
}
