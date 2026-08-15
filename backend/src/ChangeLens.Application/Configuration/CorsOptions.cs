namespace ChangeLens.Application.Configuration;

/// <summary>
/// CORS policy for the SPA. Never AllowAnyOrigin: only the configured frontend
/// origin(s) may call the API. Production deployments proxy the SPA through nginx
/// (same-origin), so CORS is primarily a development convenience — but the policy
/// is still explicit and environment-driven.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Semicolon-separated allowed origins, e.g. "http://localhost:5173;http://localhost:8080".
    /// Empty = no cross-origin browser calls allowed (same-origin via nginx proxy).
    /// </summary>
    public string AllowedOrigins { get; set; } = string.Empty;
}
