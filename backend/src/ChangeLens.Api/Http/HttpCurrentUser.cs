using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Security;

namespace ChangeLens.Api.Http;

/// <summary>Resolves the authenticated caller from the request's claims principal.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId
    {
        get
        {
            var user = accessor.HttpContext?.User;
            var value = user?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                        ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }

    public bool IsGlobalAdmin => accessor.HttpContext?.User?.IsInRole(Roles.Admin) ?? false;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
