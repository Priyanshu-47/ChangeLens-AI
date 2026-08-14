using ChangeLens.Api.Http;
using ChangeLens.Application.Dtos;
using ChangeLens.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChangeLens.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController(AuditLogService audit, ProjectAccessService access) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AuditLogResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] Guid projectId,
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

        // Audit trails are sensitive: Owner/Admin (or global admin) only.
        await access.RequireAsync(
            projectId, User.GetUserId(), User.IsInRole(Application.Security.Roles.Admin),
            ProjectPermission.Manage, ct);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await audit.QueryAsync(projectId, page, pageSize, ct);
        Response.Headers["X-Total-Count"] = result.Total.ToString();

        return Ok(new { items = result.Items, page = result.Page, pageSize = result.PageSize, total = result.Total });
    }
}
