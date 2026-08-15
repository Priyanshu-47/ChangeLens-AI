using System.Collections.ObjectModel;

namespace ChangeLens.Infrastructure.Analysis;

/// <summary>
/// In-memory dependency graph (docs/rag-architecture.md §7). PostgreSQL remains the
/// only database; the graph is a derived artifact rebuilt from the repository, never
/// persisted to a graph store (brief §8).
/// </summary>
public sealed class DependencyGraph
{
    private readonly IReadOnlyDictionary<string, SymbolInfo> _nodes;
    private readonly IReadOnlyList<DependencyEdge> _edges;

    // Adjacency: from -> outgoing targets; to -> incoming sources.
    private readonly IReadOnlyDictionary<string, List<string>> _outgoing;
    private readonly IReadOnlyDictionary<string, List<string>> _incoming;

    public DependencyGraph(
        IReadOnlyDictionary<string, SymbolInfo> nodes,
        IReadOnlyList<DependencyEdge> edges)
    {
        _nodes = nodes;
        _edges = edges;

        var outgoing = new Dictionary<string, List<string>>();
        var incoming = new Dictionary<string, List<string>>();
        foreach (var edge in edges)
        {
            Add(outgoing, edge.FromSymbolId, edge.ToSymbolId);
            Add(incoming, edge.ToSymbolId, edge.FromSymbolId);
        }

        _outgoing = new ReadOnlyDictionary<string, List<string>>(outgoing);
        _incoming = new ReadOnlyDictionary<string, List<string>>(incoming);
    }

    public IReadOnlyDictionary<string, SymbolInfo> Nodes => _nodes;

    public IReadOnlyList<DependencyEdge> Edges => _edges;

    public SymbolInfo? Find(string symbolId) => _nodes.TryGetValue(symbolId, out var s) ? s : null;

    /// <summary>Symbols this symbol references (its direct dependencies).</summary>
    public IReadOnlyList<SymbolInfo> GetDirectDependencies(string symbolId)
        => Resolve(_outgoing, symbolId);

    /// <summary>Symbols that reference this symbol (its direct dependents).</summary>
    public IReadOnlyList<SymbolInfo> GetDirectDependents(string symbolId)
        => Resolve(_incoming, symbolId);

    /// <summary>
    /// Related symbols within `depth` hops, following dependents (the impact direction:
    /// who is affected when this symbol changes) plus the symbol's own direct dependencies.
    /// Returns a deterministic breadth-first traversal.
    /// </summary>
    public IReadOnlyList<SymbolInfo> GetRelatedSymbols(string symbolId, int depth = 2)
    {
        var seen = new HashSet<string> { symbolId };
        var result = new List<SymbolInfo>();
        var frontier = new List<string> { symbolId };

        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var next = new List<string>();
            foreach (var current in frontier)
            {
                foreach (var dependent in GetDirectDependents(current))
                {
                    if (seen.Add(dependent.SymbolId))
                    {
                        result.Add(dependent);
                        next.Add(dependent.SymbolId);
                    }
                }

                foreach (var dependency in GetDirectDependencies(current))
                {
                    if (seen.Add(dependency.SymbolId))
                    {
                        result.Add(dependency);
                        next.Add(dependency.SymbolId);
                    }
                }
            }

            frontier = next;
        }

        return result;
    }

    /// <summary>Edges that touch any of the given symbol ids (evidence for the report).</summary>
    public IReadOnlyList<DependencyEdge> EdgesTouching(IReadOnlyCollection<string> symbolIds)
        => _edges.Where(e => symbolIds.Contains(e.FromSymbolId) || symbolIds.Contains(e.ToSymbolId)).ToList();

    private IReadOnlyList<SymbolInfo> Resolve(IReadOnlyDictionary<string, List<string>> adjacency, string symbolId)
    {
        if (!adjacency.TryGetValue(symbolId, out var targets))
        {
            return Array.Empty<SymbolInfo>();
        }

        return targets
            .Where(_nodes.ContainsKey)
            .Select(id => _nodes[id])
            .OrderBy(s => s.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();
    }

    private static void Add(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<string>();
            map[key] = list;
        }

        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }
}
