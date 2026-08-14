using Microsoft.Extensions.Configuration;

namespace AcmePay.Application.Auth;

public sealed record ApiKeyPrincipal(string PartnerId, bool CanRefund);

/// <summary>Validates partner API keys against the configured key store.</summary>
public sealed class ApiKeyValidator(IConfiguration configuration)
{
    private static readonly Dictionary<string, ApiKeyPrincipal> Seed =
        new(StringComparer.Ordinal)
        {
            ["acme-partner-1"] = new("partner-1", CanRefund: true),
            ["acme-partner-2"] = new("partner-2", CanRefund: false)
        };

    public ApiKeyPrincipal? Authenticate(string apiKey)
    {
        // In a real deployment this is a lookup against a vault/DB, not a seed dict.
        return Seed.TryGetValue(apiKey, out var principal) ? principal : null;
    }
}
