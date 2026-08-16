using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AcmePay.Application.Auth;

/// <summary>
/// Issues JWTs for internal service-to-service calls.
///
/// Key rotation: the signing key comes from configuration (Jwt:SigningKey). When the
/// key is rotated, the previous key MUST remain in the Jwt:SigningKeys (history) list
/// until all in-flight tokens expire, otherwise callers see "invalid signature" (401)
/// for up to the token lifetime. See runbook: authentication-failure.
/// </summary>
public sealed class TokenService(IConfiguration configuration)
{
    /// <summary>
    /// Key rotation: `Auth:JwtSigningKeys` is an ordered, comma-separated list. The
    /// FIRST key signs new tokens; ALL keys in the list validate in-flight tokens. When
    /// a key is rotated the previous key MUST remain in the list until every in-flight
    /// token has expired, otherwise callers see "invalid signature" (401) for up to the
    /// token lifetime. See runbook: authentication-failure.
    /// </summary>
    public string IssueServiceToken(string serviceName, TimeSpan lifetime)
    {
        var current = SigningKeys()[0]; // newest key signs

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, serviceName),
            new Claim(JwtRegisteredClaimNames.Iss, configuration["Auth:JwtIssuer"]!),
            new Claim("scope", "payments:write")
        };

        var signing = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(current)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: configuration["Auth:JwtIssuer"],
            audience: configuration["Auth:JwtAudience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: signing);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Validates a service token against the FULL key history (rotation-safe).</summary>
    public bool TryValidateServiceToken(string token, out string? serviceName)
    {
        foreach (var key in SigningKeys())
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidIssuer = configuration["Auth:JwtIssuer"],
                ValidAudience = configuration["Auth:JwtAudience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            try
            {
                var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);
                serviceName = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                return true;
            }
            catch (SecurityTokenException)
            {
                // wrong key (or expired) — try the previous key in the rotation history
            }
        }

        serviceName = null;
        return false;
    }

    /// <summary>
    /// Rotation observability: returns a stable fingerprint of the CURRENT signing key so
    /// monitoring can detect a rotation even when the key value is redacted from logs.
    /// The fingerprint is the hex SHA-256 of the key — safe to log.
    /// </summary>
    public string CurrentSigningKeyFingerprint()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKeys()[0]);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private IReadOnlyList<string> SigningKeys()
    {
        var raw = configuration["Auth:JwtSigningKeys"] ?? configuration["Auth:JwtSigningKey"]
            ?? throw new InvalidOperationException("Auth:JwtSigningKeys is not configured.");

        return ParseSigningKeys(raw);
    }

    /// <summary>Parses the ordered, comma-separated key-history list (unit-testable).</summary>
    public static IReadOnlyList<string> ParseSigningKeys(string raw)
        => raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
