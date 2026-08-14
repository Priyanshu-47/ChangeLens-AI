using AcmePay.Application.Auth;

namespace AcmePay.Api.Middleware;

/// <summary>
/// Authenticates partner calls with an API key from the X-Api-Key header.
/// Returns 401 when the key is missing or invalid, 403 when the key is valid
/// but lacks permission for the requested operation.
/// </summary>
public sealed class ApiKeyAuthMiddleware(
    RequestDelegate next,
    ApiKeyValidator apiKeys,
    ILogger<ApiKeyAuthMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.StartsWith("/api/v1/payments", StringComparison.OrdinalIgnoreCase))
        {
            if (!context.Request.Headers.TryGetValue("X-Api-Key", out var key) ||
                string.IsNullOrWhiteSpace(key))
            {
                logger.LogWarning("Request to {Path} without an API key", path);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "api_key_missing" });
                return;
            }

            var principal = apiKeys.Authenticate(key!);
            if (principal is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "api_key_invalid" });
                return;
            }

            if (path.EndsWith("/refunds", StringComparison.OrdinalIgnoreCase) &&
                !principal.CanRefund)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "refunds_not_allowed" });
                return;
            }

            context.Items["ApiKeyPrincipal"] = principal;
        }

        await next(context);
    }
}
