using AcmePay.External.PaymentGateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcmePay.External;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcmePayExternal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PaymentGatewayOptions>(
            configuration.GetSection(PaymentGatewayOptions.SectionName));

        services.AddHttpClient<StripeGatewayClient>();
        services.AddHttpClient<PayoutGatewayClient>();

        return services;
    }
}
