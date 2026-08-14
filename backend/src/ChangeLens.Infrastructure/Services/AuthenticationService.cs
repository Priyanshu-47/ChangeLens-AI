using ChangeLens.Application.Dtos;
using ChangeLens.Application.Exceptions;
using ChangeLens.Application.Ports;
using ChangeLens.Application.Security;
using ChangeLens.Application.Services;
using ChangeLens.Domain.Audit;
using ChangeLens.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace ChangeLens.Infrastructure.Services;

public sealed class AuthenticationService(
    UserManager<ApplicationUser> users,
    ITokenService tokens,
    AuditLogService audit,
    ICurrentUser currentUser)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? email.Split('@')[0]
                : request.DisplayName.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            // Local MVP: no email verification; JWT is issued immediately.
            EmailConfirmed = true
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw MapIdentityErrors(result);
        }

        await users.AddToRoleAsync(user, Roles.Engineer);

        await audit.WriteAsync(
            AuditActions.UserRegistered, "User", user.Id, null, user.Id, currentUser.IpAddress,
            new { user.Email }, ct);

        return await BuildAuthResponseAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());

        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
        {
            await audit.WriteAsync(
                AuditActions.LoginFailed, "User", user?.Id, null, user?.Id, currentUser.IpAddress,
                new { email = request.Email }, ct);
            throw new UnauthorizedException("Invalid email or password.");
        }

        await audit.WriteAsync(
            AuditActions.LoginSucceeded, "User", user.Id, null, user.Id, currentUser.IpAddress,
            new { user.Email }, ct);

        return await BuildAuthResponseAsync(user, ct);
    }

    public async Task<UserResponse> GetMeAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User not found.");

        return await ToUserResponseAsync(user, ct);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = (await users.GetRolesAsync(user)).ToList();
        var token = tokens.CreateToken(user.Id, user.Email ?? string.Empty, user.DisplayName, roles);

        return new AuthResponse
        {
            AccessToken = token.AccessToken,
            ExpiresInSeconds = token.ExpiresInSeconds,
            TokenType = "Bearer",
            User = new UserResponse
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                Roles = roles
            }
        };
    }

    private async Task<UserResponse> ToUserResponseAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = (await users.GetRolesAsync(user)).ToList();
        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            Roles = roles
        };
    }

    private static ChangeLensException MapIdentityErrors(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
        {
            return new ConflictException("An account with this email already exists.");
        }

        return new ValidationException(
            string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
