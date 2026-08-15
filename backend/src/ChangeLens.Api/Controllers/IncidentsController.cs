using ChangeLens.Application.Dtos;
using ChangeLens.Application.Services;
using ChangeLens.Domain.Incidents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChangeLens.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/incidents")]
public sealed class IncidentsController(
    IncidentService incidents,
    IncidentInvestigationService investigations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<IncidentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateIncidentRequest request, CancellationToken ct)
    {
        var created = await incidents.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { incidentId = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType<IncidentListItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] Guid projectId,
        [FromQuery] IncidentStatus? status = null,
        [FromQuery] IncidentSeverity? severity = null,
        [FromQuery] Guid? affectedServiceId = null,
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

        var result = await incidents.ListAsync(projectId, status, severity, affectedServiceId, page, pageSize, ct);
        Response.Headers["X-Total-Count"] = result.Total.ToString();

        return Ok(new { items = result.Items, page = result.Page, pageSize = result.PageSize, total = result.Total });
    }

    [HttpGet("{incidentId:guid}")]
    [ProducesResponseType<IncidentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid incidentId, CancellationToken ct)
        => Ok(await incidents.GetAsync(incidentId, ct));

    [HttpPatch("{incidentId:guid}")]
    [ProducesResponseType<IncidentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(Guid incidentId, [FromBody] UpdateIncidentRequest request, CancellationToken ct)
        => Ok(await incidents.UpdateAsync(incidentId, request, ct));

    /// <summary>
    /// Phase 5: submit an asynchronous incident investigation (api-contract.md §5).
    /// Returns 202 Accepted with the analysis id; poll GET /api/v1/analyses/{id}.
    /// A client RequestId is idempotent while the run is outstanding.
    /// </summary>
    [HttpPost("{incidentId:guid}/investigate")]
    [ProducesResponseType<InvestigationAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Investigate(
        Guid incidentId,
        [FromBody] InvestigateIncidentRequest? request,
        CancellationToken ct)
    {
        var accepted = await investigations.SubmitAsync(
            incidentId, request ?? new InvestigateIncidentRequest(), ct);
        return Accepted($"/api/v1/analyses/{accepted.AnalysisId}", accepted);
    }

    [HttpPost("{incidentId:guid}/events")]
    [ProducesResponseType<IncidentEventResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddEvent(Guid incidentId, [FromBody] CreateIncidentEventRequest request, CancellationToken ct)
    {
        var created = await incidents.AddEventAsync(incidentId, request, ct);
        return Created($"/api/v1/incidents/{incidentId}", created);
    }
}
