using ChangeLens.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ChangeLens.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ProjectAccessService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<RepositoryService>();
        services.AddScoped<ServiceService>();
        services.AddScoped<IncidentService>();
        services.AddScoped<ChangeRiskAnalysisService>();
        services.AddScoped<IncidentInvestigationService>();
        services.AddScoped<IncidentInvestigationOrchestrator>();

        return services;
    }
}
