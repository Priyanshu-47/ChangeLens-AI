namespace ChangeLens.Application.Tools;

/// <summary>
/// Authorization/execution context for one tool call. The project id comes from the
/// authenticated analysis run — NEVER from AI-supplied arguments — so the AI cannot
/// alter project scope (docs/agent-tools.md §7).
/// </summary>
public sealed record ToolExecutionContext(
    Guid AnalysisRunId,
    Guid ProjectId,
    Guid IncidentId,
    CancellationToken CancellationToken);
