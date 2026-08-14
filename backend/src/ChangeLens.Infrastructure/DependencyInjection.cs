using ChangeLens.Application.Ports;
using ChangeLens.Infrastructure.Identity;
using ChangeLens.Infrastructure.Options;
using ChangeLens.Infrastructure.Persistence;
using ChangeLens.Infrastructure.Seeding;
using ChangeLens.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChangeLens.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured. " +
                "Set ConnectionStrings__DefaultConnection or add it to appsettings.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npg =>
                npg.MigrationsAssembly(typeof(AppDbContext).Assembly)));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<ApplicationUser>(identity =>
            {
                identity.Password.RequiredLength = 8;
                identity.Password.RequireDigit = true;
                identity.Password.RequireUppercase = true;
                identity.Password.RequireLowercase = true;
                identity.Password.RequireNonAlphanumeric = false;
                identity.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<SeedData>();

        services.AddHttpClient<IAiServiceClient, AiServiceClient>((sp, client) =>
        {
            var ai = sp.GetRequiredService<IOptions<AiOptions>>().Value;

            if (!Uri.TryCreate(ai.BaseUrl, UriKind.Absolute, out var baseUri)
                || baseUri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException(
                    $"AI:BaseUrl must be an absolute http(s) URL. Set AI__BASEURL. (Current value: '{ai.BaseUrl}')");
            }

            client.BaseAddress = baseUri;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, ai.TimeoutSeconds));
        });

        return services;
    }
}
