using System.Text.Json;

namespace ChangeLens.Application.Tools;

/// <summary>
/// Result of one tool call (docs/agent-tools.md §14). Tool output is UNTRUSTED DATA
/// for the model: it is a sanitized JSON string, never raw exceptions or secrets.
/// <see cref="EvidenceIds"/> declares which ids in the output are citable evidence;
/// the grounding validator admits only those.
/// </summary>
public sealed record ToolExecutionResult(
    ToolStatus Status,
    string? OutputJson,
    IReadOnlyList<string> EvidenceIds,
    string? ErrorCode = null,
    long DurationMs = 0)
{
    /// <summary>Serializes the output payload with the evidence ids attached (camelCase).</summary>
    public static string SerializePayload(object payload, IReadOnlyList<string> evidenceIds)
    {
        var wrapped = new
        {
            evidenceIds,
            payload
        };
        return JsonSerializer.Serialize(wrapped, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static ToolExecutionResult Executed(string outputJson, IReadOnlyList<string> evidenceIds)
        => new(ToolStatus.Executed, outputJson, evidenceIds);

    public static ToolExecutionResult Rejected(string errorCode, string message) => new(
        ToolStatus.Rejected,
        JsonSerializer.Serialize(new { error = message }),
        [],
        ErrorCode: errorCode);

    public static ToolExecutionResult Failed(string errorCode, string message) => new(
        ToolStatus.Failed,
        JsonSerializer.Serialize(new { error = message }),
        [],
        ErrorCode: errorCode);
}

/// <summary>executed | rejected | failed | not_allowed | timeout — the wire vocabulary.</summary>
public enum ToolStatus
{
    Executed,
    Rejected,
    Failed,
    NotAllowed,
    Timeout
}
