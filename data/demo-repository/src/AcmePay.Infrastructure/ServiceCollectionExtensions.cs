using AcmePay.Infrastructure.Configuration;
using AcmePay.Infrastructure.Persistence;
using AcmePay.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AcmePay.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcmePayInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<PaymentDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Payments")));

        services.AddScoped<PaymentsRepository>();
        services.AddSingleton<ResilientExecutor>();

        services.AddOptions<ResilienceOptions>()
            .Bind(configuration.GetSection(ResilienceOptions.SectionName));

        return services;
    }
}
