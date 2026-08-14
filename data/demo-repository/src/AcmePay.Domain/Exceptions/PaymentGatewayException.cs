namespace AcmePay.Domain.Exceptions;

/// <summary>
/// Raised when the payment gateway is unreachable, times out, or returns a 5xx.
/// HTTP 401/402/403 from the gateway are mapped to DeclineReason instead — only
/// transport-level and 5xx failures are treated as retryable infrastructure faults.
/// </summary>
public sealed class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentGatewayException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public PaymentGatewayException(string message, int? statusCode, Exception inner)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }

    public bool IsRetryable =>
        StatusCode is null || StatusCode is >= 500 or 408 or 429;
}
