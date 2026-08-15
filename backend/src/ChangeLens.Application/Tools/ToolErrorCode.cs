namespace ChangeLens.Application.Tools;

/// <summary>Typed tool errors surfaced to the AI (safe, structured — never raw exceptions).</summary>
public static class ToolErrorCode
{
    /// <summary>The model proposed a tool name outside the allowlist (docs/agent-tools.md §4).</summary>
    public const string NotAllowed = "TOOL_NOT_ALLOWED";

    /// <summary>Arguments failed schema/type validation (reject before execution).</summary>
    public const string InvalidArgument = "INVALID_ARGUMENT";

    /// <summary>Project isolation: the requested resource does not belong to this project.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>The tool did not complete within the configured per-tool timeout.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>Internal execution error (read-only tools should rarely hit this).</summary>
    public const string ToolError = "TOOL_ERROR";
}
