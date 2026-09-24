using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Audit.Application;
using UZLLM.Modules.Audit.Contracts;

namespace UZLLM.Modules.Audit.Infrastructure;

public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmAudit(this IServiceCollection services)
    {
        services.AddScoped<IAuditEventStore, PostgreSqlAuditEventStore>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        return services;
    }
}
