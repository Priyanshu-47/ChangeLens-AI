using System.Text.Json;

namespace ChangeLens.Application.Tools;

/// <summary>
/// A read-only, allowlisted tool (docs/agent-tools.md §3–5). The AI may only PROPOSE
/// a call; the application validates arguments, authorizes the project scope, executes,
/// sanitizes the result, and audits it. Tools never touch PostgreSQL directly, never run
/// shell commands, never access arbitrary files or URLs.
/// </summary>
public interface ITool
{
    /// <summary>Stable tool name (the only name the AI may propose).</summary>
    string Name { get; }

    /// <summary>Model-facing description (what the tool does, when to use it).</summary>
    string Description { get; }

    ToolRiskLevel RiskLevel { get; }

    /// <summary>JSON Schema for arguments, sent to the AI service for prompting.</summary>
    Dictionary<string, object?> InputSchema { get; }

    /// <summary>
    /// Validates and executes the call within <paramref name="context"/>. Returns a
    /// structured <see cref="ToolExecutionResult"/> — validation failures and
    /// authorization failures are results, never exceptions. Project isolation is
    /// enforced here from the context's project id (never from arguments).
    /// </summary>
    Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken ct);
}
