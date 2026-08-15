using System.Text.Json;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Incidents;

namespace ChangeLens.Application.Tools;

/// <summary>
/// get_incident_timeline — chronological IncidentEvent entries for a project-scoped
/// incident (LOW risk, read-only). Each event carries a stable evidence id
/// (`incident-event:<guid>`) so conclusions can cite specific timeline entries.
/// </summary>
public sealed class GetIncidentTimelineTool(IAppDbContext db) : ITool
{
    public string Name => "get_incident_timeline";

    public string Description =>
        "Returns the chronological timeline of events (deployment, error, alert, mitigation, ...) for a given incidentId.";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["incidentId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" },
            ["limit"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 200 }
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

        if (!ToolArguments.TryInt(arguments, "limit", 1, 200, 50, out var limit, out error))
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.InvalidArgument, error!);
        }

        var events = await db.Set<IncidentEvent>()
            .AsNoTracking()
            .Where(e => e.IncidentId == incidentId)
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id)
            .Take(limit)
            .ToListAsync(ct);

        // Project isolation: verify the incident belongs to the analysis project
        // (the event query above is already incident-scoped; this closes the gap when
        // the incident id does not exist or belongs to another project).
        var belongsToProject = await db.Set<Incident>()
            .AsNoTracking()
            .AnyAsync(i => i.Id == incidentId && i.ProjectId == context.ProjectId, ct);
        if (!belongsToProject)
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.NotFound, "Incident not found in this project.");
        }

        var ids = events.Select(e => $"incident-event:{e.Id:N}").ToList();
        var payload = new
        {
            incidentId,
            events = events.Select(e => new
            {
                evidenceId = $"incident-event:{e.Id:N}",
                occurredAtUtc = e.OccurredAtUtc,
                type = e.Type.ToString(),
                source = Truncate(e.Source, 500),
                message = Truncate(e.Message, 2000)
            })
        };
        return ToolExecutionResult.Executed(
            ToolExecutionResult.SerializePayload(payload, ids),
            ids);
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];
}
