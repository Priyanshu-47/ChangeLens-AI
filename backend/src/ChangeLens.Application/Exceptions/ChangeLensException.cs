namespace ChangeLens.Application.Exceptions;

/// <summary>
/// Base for expected, user-facing failures. The exception middleware maps these to
/// ProblemDetails responses; <see cref="Code"/> is the machine-readable error code.
/// </summary>
public abstract class ChangeLensException : Exception
{
    protected ChangeLensException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }

    /// <summary>Optional structured payload (e.g. AI validation details). Included in the ProblemDetails body.</summary>
    public virtual object? Details => null;
}
