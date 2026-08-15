using System.Text.Json;
using ChangeLens.Application.Ports;
using ChangeLens.Domain.Services;

namespace ChangeLens.Application.Tools;

/// <summary>
/// get_service — returns a project-scoped service record (LOW risk, read-only).
/// Cross-project service ids resolve to NOT_FOUND (project isolation, brief §7).
/// </summary>
public sealed class GetServiceTool(IAppDbContext db) : ITool
{
    public string Name => "get_service";

    public string Description =>
        "Returns the service record (name, language, root path) for a given serviceId. Use to confirm which service an incident or symbol belongs to.";

    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public Dictionary<string, object?> InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object?>
        {
            ["serviceId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" }
        },
        ["required"] = new object[] { "serviceId" }
    };

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context, JsonElement arguments, CancellationToken ct)
    {
        if (!ToolArguments.TryGuid(arguments, "serviceId", out var serviceId, out var error))
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.InvalidArgument, error!);
        }

        var service = await db.Set<Service>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.ProjectId == context.ProjectId, ct);

        if (service is null)
        {
            return ToolExecutionResult.Rejected(ToolErrorCode.NotFound, "Service not found in this project.");
        }

        var payload = new
        {
            serviceId = service.Id,
            name = service.Name,
            language = service.Language,
            rootPath = service.RootPath
        };
        var id = $"service:{service.Id:N}";
        return ToolExecutionResult.Executed(
            ToolExecutionResult.SerializePayload(payload, [id]),
            [id]);
    }
}
