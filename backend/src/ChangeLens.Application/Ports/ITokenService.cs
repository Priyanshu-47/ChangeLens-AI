namespace ChangeLens.Application.Ports;

/// <summary>Issues JWT access tokens for an authenticated user.</summary>
public interface ITokenService
{
    TokenResult CreateToken(Guid userId, string email, string displayName, IReadOnlyList<string> roles);
}

public sealed record TokenResult(string AccessToken, int ExpiresInSeconds);
