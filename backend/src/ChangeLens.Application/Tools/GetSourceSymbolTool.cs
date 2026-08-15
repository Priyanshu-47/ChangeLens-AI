using System.Text.Json;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Ports;

namespace ChangeLens.Application.Tools;

/// <summary>
/// get_source_symbol — retrieves source chunks for a symbol from the project's source
/// model (documentType=SourceCode). Accepts an identifier only, never a filesystem path:
/// traversal, drive letters, and URI schemes are rejected (docs/agent-tools.md §10).
/// </summary>
public sealed class GetSourceSymbolTool(IAiServiceClient aiClient) : ITool
{
    public string Name => "get_source_symbol";

    public string Description =>
        "Retrieves the source code of a project symbol by name (e.g. TokenService, IssueServiceToken). Accepts a symbol identifier, never a file path.";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["symbol"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 200 },
            ["topK"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 10 }
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

        if (!ToolArguments.TryInt(arguments, "topK", 1, 10, 5, out var topK, out error))
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.InvalidArgument, error!);
        }

        var response = await aiClient.RetrievalSearchAsync(new RetrievalSearchRequestDto
        {
            ProjectId = context.ProjectId,
            Query = symbol,
            DocumentTypes = ["SourceCode"],
            K = topK
        }, ct);

        var items = response.Results.Select(r => new
        {
            id = $"chunk:{r.ChunkId}",
            path = r.Metadata.GetValueOrDefault("path") as string,
            language = r.Metadata.GetValueOrDefault("language") as string,
            score = r.Score,
            content = GetRunbookTool.Truncate(r.Content, 8000)
        }).ToList();
        var ids = items.Select(i => i.id).ToList();
        var payload = new { symbol, items };
        return ToolExecutionResult.Executed(
            ToolExecutionResult.SerializePayload(payload, ids),
            ids);
    }
}
