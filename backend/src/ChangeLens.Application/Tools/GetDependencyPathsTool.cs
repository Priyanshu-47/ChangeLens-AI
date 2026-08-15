using System.Text.Json;
using ChangeLens.Application.Ports;

namespace ChangeLens.Application.Tools;

/// <summary>
/// get_dependency_paths — bounded traversal of the Roslyn dependency graph for a
/// symbol (docs/agent-tools.md §11). maxDepth is capped (≤ 4); traversal never leaves
/// the analyzer's in-memory graph. Each path carries an evidence id
/// (`dependency:<from> -> <to>`) matching the change-risk evidence convention.
/// </summary>
public sealed class GetDependencyPathsTool(IChangeAnalysisEngine changeEngine) : ITool
{
    public string Name => "get_dependency_paths";

    public string Description =>
        "Returns dependency paths for a symbol in the repository dependency graph (who depends on it, what it depends on), up to a bounded depth. Use to reason about impact.";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["symbol"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 200 },
            ["maxDepth"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 4 }
        },
        ["required"] = new object[] { "symbol" }
    };

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context, JsonElement arguments, CancellationToken ct)
    {
        if (!ToolArguments.TryString(arguments, "symbol", 200, out var symbol, out var error))
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.InvalidArgument, error!);
        }

        if (!ToolArguments.IsSafeIdentifier(symbol))
        {
            return ToolExecutionResult.Rejected(
                ToolErrorCode.InvalidArgument, "Symbol must be an identifier, not a path or URL.");
        }

        if (!ToolArguments.TryInt(arguments, "maxDepth", 1, 4, 2, out var maxDepth, out error))
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.InvalidArgument, error!);
        }

        var paths = await Task.Run(() => changeEngine.FindDependencyPaths(symbol, maxDepth), ct);
        var ids = paths.Paths
            .Select(p => $"dependency:{p.From} -> {p.To}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var payload = new
        {
            symbol,
            resolvedSymbol = paths.ResolvedSymbol,
            maxDepth,
            paths = paths.Paths.Select(p => new
            {
                from = p.From,
                to = p.To,
                edgeType = p.EdgeType,
                filePath = p.FilePath
            }),
            warnings = paths.Warnings
        };
        return ToolExecutionResult.Executed(
            ToolExecutionResult.SerializePayload(payload, ids),
            ids);
    }
}
