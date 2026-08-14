using ChangeLens.Application.Dtos;
using ChangeLens.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChangeLens.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class RepositoriesController(RepositoryService repositories) : ControllerBase
{
    [HttpPost("projects/{projectId:guid}/repositories")]
    [ProducesResponseType<RepositoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Register(Guid projectId, [FromBody] CreateRepositoryRequest request, CancellationToken ct)
    {
        var created = await repositories.RegisterAsync(projectId, request, ct);
        return CreatedAtAction(nameof(Get), new { repositoryId = created.Id }, created);
    }

    [HttpGet("projects/{projectId:guid}/repositories")]
    [ProducesResponseType<RepositoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await repositories.ListAsync(projectId, page, pageSize, ct);
        Response.Headers["X-Total-Count"] = result.Total.ToString();

        return Ok(new { items = result.Items, page = result.Page, pageSize = result.PageSize, total = result.Total });
    }

    [HttpGet("repositories/{repositoryId:guid}")]
    [ProducesResponseType<RepositoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid repositoryId, CancellationToken ct)
        => Ok(await repositories.GetAsync(repositoryId, ct));
}
