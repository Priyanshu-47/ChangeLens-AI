using ChangeLens.Application.Dtos;
using ChangeLens.Domain.Incidents;

namespace ChangeLens.Application.Services;

/// <summary>
/// Builds the normalized investigation context from the domain Incident (brief §12).
/// Nothing is fabricated: missing data becomes explicit unknowns; timeline entries
/// preserve chronological order; event raw data is capped because it is untrusted.
/// </summary>
public static class IncidentContextBuilder
{
    public static IncidentContextDto Build(Incident incident)
    {
        var events = incident.Events
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id)
            .ToList();

        var context = new IncidentContextDto
        {
            Title = incident.Title,
            Summary = incident.Summary,
            Severity = incident.Severity.ToString(),
            Status = incident.Status.ToString(),
            Environment = incident.Environment,
            Service = incident.AffectedService?.Name,
            StartedAtUtc = incident.StartedAtUtc,
            DetectedAtUtc = incident.DetectedAtUtc,
            Timeline = events.Select(ToTimelineEvent).ToList()
        };

        // Symptoms come from the actual timeline: error/log messages (brief §13 keeps
        // exact identifiers — error type, status codes, exception names — searchable).
        context.Symptoms = events
            .Where(e => e.Type is IncidentEventType.Error or IncidentEventType.Log)
            .Select(e => e.Message)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Take(20)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        context.KnownFacts =
        [
            $"Incident: {incident.Title}",
            $"Severity: {incident.Severity}",
            $"Started at: {incident.StartedAtUtc:O}"
        ];
        if (!string.IsNullOrWhiteSpace(incident.Environment))
        {
            context.KnownFacts.Add($"Environment: {incident.Environment}");
        }

        if (incident.AffectedService is not null)
        {
            context.KnownFacts.Add($"Affected service: {incident.AffectedService.Name}");
        }

        if (incident.DetectedAtUtc is not null)
        {
            context.KnownFacts.Add($"Detected at: {incident.DetectedAtUtc:O}");
        }

        // Explicit unknowns — never fabricated telemetry (brief §19).
        if (incident.DetectedAtUtc is null)
        {
            context.Unknowns.Add("No detection timestamp was supplied.");
        }

        if (events.All(e => e.Type != IncidentEventType.Deployment))
        {
            context.Unknowns.Add("No deployment events were supplied; the change window is unknown.");
        }

        if (context.Symptoms.Count == 0)
        {
            context.Unknowns.Add("No error or log samples were supplied.");
        }

        if (events.All(e => e.Type != IncidentEventType.Metric))
        {
            context.Unknowns.Add("No metric/telemetry data was available.");
        }

        return context;
    }

    private static TimelineEventDto ToTimelineEvent(IncidentEvent e) => new()
    {
        OccurredAtUtc = e.OccurredAtUtc,
        Type = e.Type.ToString(),
        Source = Truncate(e.Source, 500),
        Message = Truncate(e.Message, 2000),
        // Raw payloads are untrusted data and large; cap aggressively (brief §22).
        RawData = Truncate(e.RawDataJson, 4000)
    };

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
