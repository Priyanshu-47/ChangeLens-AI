using ChangeLens.Application.Dtos;
using ChangeLens.Application.Security;
using ChangeLens.Application.Services;
using ChangeLens.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ChangeLens.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController(ProjectService projects, UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin,Engineer")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        var created = await projects.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { projectId = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await projects.ListAsync(page, pageSize, ct);
        Response.Headers["X-Total-Count"] = result.Total.ToString();

        return Ok(new { items = result.Items, page = result.Page, pageSize = result.PageSize, total = result.Total });
    }

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken ct)
        => Ok(await projects.GetAsync(projectId, ct));

    [HttpPatch("{projectId:guid}")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(Guid projectId, [FromBody] UpdateProjectRequest request, CancellationToken ct)
        => Ok(await projects.UpdateAsync(projectId, request, ct));

    [HttpPost("{projectId:guid}/members")]
    [ProducesResponseType<MemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMember(Guid projectId, [FromBody] AddMemberRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim().ToLowerInvariant())
            ?? throw new Application.Exceptions.NotFoundException("User not found.");

        var member = await projects.AddMemberAsync(
            projectId, user.Id, user.Email ?? string.Empty, user.DisplayName, request.Role, ct);

        return StatusCode(StatusCodes.Status201Created, member);
    }

    [HttpDelete("{projectId:guid}/members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMember(Guid projectId, Guid userId, CancellationToken ct)
    {
        await projects.RemoveMemberAsync(projectId, userId, ct);
        return NoContent();
    }
}
