using System.Text.Json;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Ports;

namespace ChangeLens.Application.Tools;

/// <summary>
/// get_runbook — retrieves project runbook chunks through the existing retrieval layer
/// (documentType=Runbook, project-scoped; docs/agent-tools.md §12). Output is bounded
/// (topK ≤ 5, content capped) and the attached chunk ids become citable evidence.
/// </summary>
public sealed class GetRunbookTool(IAiServiceClient aiClient) : ITool
{
    public string Name => "get_runbook";

    public string Description =>
        "Retrieves the project runbook most relevant to a query. Use when the incident matches a known runbook (e.g. an authentication-failure runbook).";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["query"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 500 },
            ["topK"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 5 }
        },
        ["required"] = new object[] { "query" }
    };

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context, JsonElement arguments, CancellationToken ct)
    {
        if (!ToolArguments.TryString(arguments, "query", 500, out var query, out var error))
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.InvalidArgument, error!);
        }

        if (!ToolArguments.TryInt(arguments, "topK", 1, 5, 3, out var topK, out error))
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.InvalidArgument, error!);
        }

        var response = await aiClient.RetrievalSearchAsync(new RetrievalSearchRequestDto
        {
            ProjectId = context.ProjectId,
            Query = query,
            DocumentTypes = ["Runbook"],
            K = topK
        }, ct);

        var items = response.Results.Select(r => new
        {
            id = $"chunk:{r.ChunkId}",
            title = r.Metadata.GetValueOrDefault("title") as string,
            path = r.Metadata.GetValueOrDefault("path") as string,
            score = r.Score,
            content = Truncate(r.Content, 8000)
        }).ToList();
        var ids = items.Select(i => i.id).ToList();
        var payload = new { query, items };
        return ToolExecutionResult.Executed(
            ToolExecutionResult.SerializePayload(payload, ids),
            ids);
    }

    internal static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Length <= max ? s : s[..max];
}
