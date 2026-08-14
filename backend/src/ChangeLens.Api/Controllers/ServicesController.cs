using ChangeLens.Application.Dtos;
using ChangeLens.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChangeLens.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class ServicesController(ServiceService services) : ControllerBase
{
    [HttpPost("projects/{projectId:guid}/services")]
    [ProducesResponseType<ServiceResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(Guid projectId, [FromBody] CreateServiceRequest request, CancellationToken ct)
    {
        var created = await services.CreateAsync(projectId, request, ct);
        return CreatedAtAction(nameof(Get), new { serviceId = created.Id }, created);
    }

    [HttpGet("projects/{projectId:guid}/services")]
    [ProducesResponseType<ServiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct)
        => Ok(await services.ListAsync(projectId, ct));

    [HttpGet("services/{serviceId:guid}")]
    [ProducesResponseType<ServiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid serviceId, CancellationToken ct)
        => Ok(await services.GetAsync(serviceId, ct));
}
