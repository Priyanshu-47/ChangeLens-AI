using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ChangeLens.Api.Http;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
