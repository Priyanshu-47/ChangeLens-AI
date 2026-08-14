namespace ChangeLens.Application.Exceptions;

/// <summary>Request conflicts with current state (duplicate email, last-owner removal, …) (409).</summary>
public sealed class ConflictException : ChangeLensException
{
    public ConflictException(string message)
        : base(409, "conflict", message)
    {
    }
}
