using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChangeLens.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
public sealed class HealthController(HealthCheckService health) : ControllerBase
{
    /// <summary>Full health report including the database check. Unauthenticated.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var report = await health.CheckHealthAsync(ct);

        var body = new
        {
            status = report.Status.ToString(),
            timestampUtc = DateTime.UtcNow,
            version = VersionProvider.Current,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1)
            })
        };

        return report.Status == HealthStatus.Healthy
            ? Ok(body)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}

public static class VersionProvider
{
    public static string Current =>
        typeof(VersionProvider).Assembly.GetName().Version?.ToString() ?? "unknown";
}
