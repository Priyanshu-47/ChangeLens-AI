namespace ChangeLens.Application.Exceptions;

/// <summary>Authentication/credential failure (401).</summary>
public sealed class UnauthorizedException : ChangeLensException
{
    public UnauthorizedException(string message)
        : base(401, "unauthorized", message)
    {
    }
}
