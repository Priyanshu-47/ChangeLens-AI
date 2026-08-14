namespace ChangeLens.Application.Exceptions;

/// <summary>Request failed validation in the service layer (400).</summary>
public sealed class ValidationException : ChangeLensException
{
    public ValidationException(string message)
        : base(400, "validation_failed", message)
    {
    }
}
