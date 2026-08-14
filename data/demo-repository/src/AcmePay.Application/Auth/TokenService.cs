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
    public string IssueServiceToken(string serviceName, TimeSpan lifetime)
    {
        var key = configuration["Auth:JwtSigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, serviceName),
            new Claim(JwtRegisteredClaimNames.Iss, configuration["Auth:JwtIssuer"]!),
            new Claim("scope", "payments:write")
        };

        var signing = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: configuration["Auth:JwtIssuer"],
            audience: configuration["Auth:JwtAudience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: signing);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
