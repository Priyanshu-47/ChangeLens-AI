using ChangeLens.Application.Dtos;
using ChangeLens.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChangeLens.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/analyses")]
public sealed class AnalysesController(
    ChangeRiskAnalysisService analyses,
    IncidentInvestigationService investigations) : ControllerBase
{
    /// <summary>
    /// Phase 6: list analysis runs for a project (api-contract.md §2 — dashboard +
    /// trace views). Optional filters: type, status, incidentId. Read permission;
    /// non-members see 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<AnalysisStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] Guid projectId,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? incidentId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (projectId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation failed",
                Detail = "The 'projectId' query parameter is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await investigations.ListAsync(projectId, type, status, incidentId, page, pageSize, ct);
        Response.Headers["X-Total-Count"] = result.Total.ToString();

        return Ok(new { items = result.Items, page = result.Page, pageSize = result.PageSize, total = result.Total });
    }

    /// <summary>
    /// Phase 5: poll an analysis job (api-contract.md §5). Returns Queued/Running/
    /// Succeeded/Failed; the validated result is included only when Succeeded. Project
    /// authorization is enforced (non-members see 404, Viewers may poll).
    /// </summary>
    [HttpGet("{analysisId:guid}")]
    [ProducesResponseType<AnalysisStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid analysisId, CancellationToken ct)
        => Ok(await investigations.GetStatusAsync(analysisId, ct));

    /// <summary>
    /// Phase 7: per-stage observability trace of an analysis (docs/evaluation.md §5).
    /// Authorization matches the analysis itself (Read; non-members see 404) — a user
    /// can never read another project's trace.
    /// </summary>
    [HttpGet("{analysisId:guid}/trace")]
    [ProducesResponseType<AnalysisTraceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Trace(Guid analysisId, CancellationToken ct)
        => Ok(await investigations.GetTraceAsync(analysisId, ct));

    /// <summary>
    /// Phase 2 vertical slice: POST /api/v1/analyses/change-risk → .NET validates and
    /// authorizes, then calls the AI service (FastAPI → Gemini) and returns the
    /// schema-validated risk report. Phase 4 converts this to the async
    /// 202 + poll job pattern from the API contract.
    /// </summary>
    [HttpPost("change-risk")]
    [EnableRateLimiting("analysis")]
    [ProducesResponseType<ChangeRiskAnalysisResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> AnalyzeChangeRisk(
        [FromBody] AnalyzeChangeRiskRequest request, CancellationToken ct)
        => Ok(await analyses.AnalyzeChangeRiskAsync(request, ct));
}
