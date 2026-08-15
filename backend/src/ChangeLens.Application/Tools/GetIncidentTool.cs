using System.Text.Json;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Incidents;

namespace ChangeLens.Application.Tools;

/// <summary>
/// get_incident — returns the project-scoped incident record (LOW risk, read-only).
/// The project boundary comes from the analysis context, never from arguments: an
/// incident belonging to another project resolves to NOT_FOUND (no existence leak).
/// </summary>
public sealed class GetIncidentTool(IAppDbContext db) : ITool
{
    public string Name => "get_incident";

    public string Description =>
        "Returns the incident record (title, severity, status, affected service, summary) for a given incidentId. Use when the incident details are needed.";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["incidentId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" }
        },
        ["required"] = new object[] { "incidentId" }
    };

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context, JsonElement arguments, CancellationToken ct)
    {
        if (!ToolArguments.TryGuid(arguments, "incidentId", out var incidentId, out var error))
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.InvalidArgument, error!);
        }

        var incident = await db.Set<Incident>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == incidentId && i.ProjectId == context.ProjectId, ct);

        if (incident is null)
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.NotFound, "Incident not found in this project.");
        }

        var payload = new
        {
            incidentId = incident.Id,
            title = incident.Title,
            severity = incident.Severity.ToString(),
            status = incident.Status.ToString(),
            classification = incident.Classification,
            environment = incident.Environment,
            service = incident.AffectedService?.Name,
            startedAtUtc = incident.StartedAtUtc,
            detectedAtUtc = incident.DetectedAtUtc,
            summary = Truncate(incident.Summary, 2000)
        };
        return ToolExecutionResult.Executed(
            ToolExecutionResult.SerializePayload(payload, [$"incident:{incident.Id:N}"]),
            [$"incident:{incident.Id:N}"]);
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];
}
