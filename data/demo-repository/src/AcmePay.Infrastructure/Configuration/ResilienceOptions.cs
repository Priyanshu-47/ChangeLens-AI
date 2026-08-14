namespace AcmePay.Infrastructure.Configuration;

/// <summary>
/// Environment-driven resilience tuning. In staging the timeouts are tighter
/// and retries fewer, which is how configuration drift manifests (see the
/// appsettings.Staging.json override and the golden dataset).
/// </summary>
public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public int GatewayTimeoutSeconds { get; set; } = 10;

    public int MaxRetries { get; set; } = 3;

    public int BaseBackoffMilliseconds { get; set; } = 200;
}
