using System.Text.Json;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Ports;

namespace ChangeLens.Application.Tools;

/// <summary>
/// search_evidence — hybrid retrieval over the project corpus (docs/agent-tools.md §13).
/// The project scope is injected from the analysis context; the AI cannot override it.
/// An optional documentType narrows the corpus; unknown types are rejected.
/// </summary>
public sealed class SearchEvidenceTool(IAiServiceClient aiClient) : ITool
{
    private static readonly string[] KnownDocumentTypes =
        ["SourceCode", "OpenApi", "Incident", "Runbook", "DeploymentRecord"];

    public string Name => "search_evidence";

    public string Description =>
        "Searches the project evidence corpus (source, incidents, runbooks, APIs) using hybrid retrieval. Use to find evidence beyond the initial package.";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["query"] = new Dictionary<string, object?> { ["type"] = "string", ["maxLength"] = 500 },
            ["documentType"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["enum"] = KnownDocumentTypes
            },
            ["topK"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 10 }
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

        if (!ToolArguments.TryInt(arguments, "topK", 1, 10, 5, out var topK, out error))
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.InvalidArgument, error!);
        }

        string? documentType = null;
        if (arguments.TryGetProperty("documentType", out var dt) && dt.ValueKind == JsonValueKind.String)
        {
            documentType = dt.GetString()?.Trim();
            if (string.IsNullOrEmpty(documentType))
            {
                documentType = null;
            }
            else if (!KnownDocumentTypes.Contains(documentType, StringComparer.Ordinal))
            {
                return ToolExecutionResult.Rejected(
                    ToolErrorCode.InvalidArgument,
                    $"documentType must be one of: {string.Join(", ", KnownDocumentTypes)}.");
            }
        }

        var response = await aiClient.RetrievalSearchAsync(new RetrievalSearchRequestDto
        {
            ProjectId = context.ProjectId,
            Query = query,
            DocumentTypes = documentType is null ? null : [documentType],
            K = topK
        }, ct);

        var items = response.Results.Select(r => new
        {
            id = $"chunk:{r.ChunkId}",
            documentType = r.DocumentType,
            title = r.Metadata.GetValueOrDefault("title") as string,
            path = r.Metadata.GetValueOrDefault("path") as string,
            score = r.Score,
            content = GetRunbookTool.Truncate(r.Content, 8000)
        }).ToList();
        var ids = items.Select(i => i.id).ToList();
        var payload = new { query, documentType, items };
        return ToolExecutionResult.Executed(
            ToolExecutionResult.SerializePayload(payload, ids),
            ids);
    }
}
