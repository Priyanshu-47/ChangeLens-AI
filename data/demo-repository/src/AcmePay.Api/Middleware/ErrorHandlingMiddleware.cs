using AcmePay.Domain.Exceptions;

namespace AcmePay.Api.Middleware;

/// <summary>Maps domain failures to problem+json without leaking internal details.</summary>
public sealed class ErrorHandlingMiddleware(
    RequestDelegate next,
    ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (PaymentGatewayException ex)
        {
            // The gateway is down or timed out; surface a retryable 502.
            logger.LogWarning(ex, "Payment gateway failure for {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new { error = "gateway_unavailable" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "internal_error" });
        }
    }
}
