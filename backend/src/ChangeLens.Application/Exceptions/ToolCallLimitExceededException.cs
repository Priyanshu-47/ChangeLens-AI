namespace ChangeLens.Application.Exceptions;

/// <summary>
/// The AI kept proposing tool calls past the configured bound (docs/agent-tools.md §21).
/// A safety failure, not a provider error: the loop is never unbounded. The orchestrator
/// maps this to a Failed(TOOL_CALL_LIMIT_EXCEEDED) run.
/// </summary>
public sealed class ToolCallLimitExceededException(int maxToolCalls)
    : ChangeLensException(500, "tool_call_limit_exceeded",
        $"The AI proposed more than {maxToolCalls} tool calls; the analysis stopped at the safety limit.")
{
    public override object? Details => new { maxToolCalls };
}
