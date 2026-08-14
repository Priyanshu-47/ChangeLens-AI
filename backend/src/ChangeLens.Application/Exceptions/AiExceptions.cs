namespace ChangeLens.Application.Exceptions;

/// <summary>
/// Base for failures coming from the internal AI service. The exception middleware maps
/// these to ProblemDetails with the upstream status code — provider stack traces and
/// internal details are never exposed.
/// </summary>
public abstract class AiServiceException : ChangeLensException
{
    protected AiServiceException(int statusCode, string code, string message)
        : base(statusCode, code, message)
    {
    }
}

/// <summary>AI service unreachable, returned an unexpected status, or an empty body (502).</summary>
public sealed class AiUnavailableException : AiServiceException
{
    public AiUnavailableException(string message)
        : base(502, "ai_service_unavailable", message)
    {
    }
}

/// <summary>Provider rate limit surfaced by the AI service (429).</summary>
public sealed class AiRateLimitedException : AiServiceException
{
    public AiRateLimitedException(string message)
        : base(429, "llm_rate_limited", message)
    {
    }
}

/// <summary>Provider did not answer in time (504).</summary>
public sealed class AiTimeoutException : AiServiceException
{
    public AiTimeoutException(string message)
        : base(504, "ai_timeout", message)
    {
    }
}

/// <summary>AI service rejected the request or its output failed validation (422).</summary>
public sealed class AiValidationFailedException : AiServiceException
{
    public AiValidationFailedException(string message, object? details = null)
        : base(422, "ai_validation_failed", message)
    {
        Details = details;
    }

    public override object? Details { get; }
}
