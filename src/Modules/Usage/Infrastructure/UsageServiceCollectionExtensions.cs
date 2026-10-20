using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Usage.Application;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Modules.Usage.Infrastructure;

public static class UsageServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmSliImport(this IServiceCollection services, SliCollectorTrust trust)
    {
        services.AddSingleton(trust);
        services.AddScoped<ISliManifestImporter, PostgreSqlSliManifestImporter>();
        return services;
    }

    public static IServiceCollection AddUzllmUsage(this IServiceCollection services)
    {
        services.AddScoped<IUsageStore, PostgreSqlUsageStore>();
        services.AddScoped<IUsageService, UsageService>();
        services.AddScoped<IUsageReadStore, PostgreSqlUsageReadStore>();
        services.AddScoped<IUsageReadService, UsageReadService>();
        services.AddScoped<IUsageExportService, UsageReadService>();
        services.AddScoped<IUsageRollupStore, PostgreSqlUsageRollupStore>();
        services.AddSingleton<PayloadEnvelopeProtector>();
        services.AddScoped<PostgreSqlPayloadRetentionService>();
        services.AddScoped<IPayloadRetentionService>(provider =>
            provider.GetRequiredService<PostgreSqlPayloadRetentionService>());
        return services;
    }

    public static IServiceCollection AddUzllmPayloadRetentionPolicies(this IServiceCollection services)
    {
        services.AddScoped<IPayloadRetentionPolicyStore>(provider =>
            provider.GetRequiredService<PostgreSqlPayloadRetentionService>());
        services.AddScoped<IPayloadRetentionPolicyService, PayloadRetentionPolicyService>();
        return services;
    }
}
