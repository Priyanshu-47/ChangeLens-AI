using ChangeLens.Application.Dtos;
using ChangeLens.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChangeLens.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/analyses")]
public sealed class AnalysesController(
    ChangeRiskAnalysisService analyses,
    IncidentInvestigationService investigations) : ControllerBase
{
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
    /// Phase 2 vertical slice: POST /api/v1/analyses/change-risk → .NET validates and
    /// authorizes, then calls the AI service (FastAPI → Gemini) and returns the
    /// schema-validated risk report. Phase 4 converts this to the async
    /// 202 + poll job pattern from the API contract.
    /// </summary>
    [HttpPost("change-risk")]
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
