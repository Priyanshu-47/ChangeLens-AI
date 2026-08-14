using ChangeLens.Application.Security;
using ChangeLens.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace ChangeLens.Infrastructure.Seeding;

/// <summary>
/// Idempotent development seeding: global roles plus the demo users documented in
/// the backend README. Enabled via configuration (Seed:Enabled, true in Development).
/// Demo-data seeding for projects/repositories/incidents arrives in Phase 3.
/// </summary>
public sealed class SeedData(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    ILogger<SeedData> logger)
{
    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        foreach (var roleName in Roles.All)
        {
            if (!await roles.RoleExistsAsync(roleName))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }

        await EnsureUserAsync("admin@changelens.dev", "Changelens Admin", "AdminPass!2026", Roles.Admin, ct);
        await EnsureUserAsync("engineer@changelens.dev", "Changelens Engineer", "EngineerPass!2026", Roles.Engineer, ct);
        await EnsureUserAsync("viewer@changelens.dev", "Changelens Viewer", "ViewerPass!2026", Roles.Viewer, ct);
    }

    private async Task EnsureUserAsync(
        string email, string displayName, string password, string role, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = displayName,
                EmailConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogError("Seeding user {Email} failed: {Errors}",
                    email, string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }
        }

        if (!await users.IsInRoleAsync(user, role))
        {
            await users.AddToRoleAsync(user, role);
        }
    }
}
