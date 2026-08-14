namespace ChangeLens.Application.Exceptions;

/// <summary>Caller is recognized but lacks permission for the operation (403).</summary>
public sealed class ForbiddenAccessException : ChangeLensException
{
    public ForbiddenAccessException(string message)
        : base(403, "forbidden", message)
    {
    }
}
