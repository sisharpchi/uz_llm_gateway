using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Domain;
using UZLLM.Persistence;

namespace UZLLM.Modules.Usage.Infrastructure;

public static class SliReportRegistration
{
    public static IServiceCollection AddUzllmSliReports(this IServiceCollection services, IConfiguration configuration)
    {
        var scopes = new Dictionary<string, SliReportScope>(StringComparer.Ordinal);
        foreach (var entry in configuration.GetSection("Sli:Reports:Scopes").GetChildren())
        {
            var scope = entry.Get<SliReportScope>() ?? throw new InvalidOperationException("Invalid SLI scope.");
            SliReportPolicy.Validate(scope);
            scopes.Add(scope.Id, scope with { Producers = scope.Producers.OrderBy(value => value.Producer, StringComparer.Ordinal).ToArray() });
        }
        services.AddSingleton(new SliReportProfiles(scopes));
        services.AddSingleton(new SliReportLimits());
        services.AddScoped<ISliReportSourceReader, PostgreSqlSliReportSourceReader>();
        services.AddScoped<ISliReportStore, PostgreSqlSliReportStore>();
        services.AddScoped<ILeasedJobHandler, SliReportBuildHandler>();
        services.AddScoped<IOutboxHandler, SliReportPublishedHandler>();
        return services;
    }
}
public sealed class SliReportBuildHandler(ISliReportStore store) : ILeasedJobHandler
{
    public string JobType => PostgreSqlSliReportStore.JobType;
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(job.Payload);
        await store.BuildAsync(json.RootElement.GetProperty("operationId").GetGuid(), job.AttemptCount, cancellationToken);
    }
}
/// <summary>Internal committed lifecycle acknowledgement, not customer notification or SLA approval.</summary>
public sealed class SliReportPublishedHandler(ITransactionCoordinator transactions, IConsumerInboxStore inbox) : IOutboxHandler
{
    public string EventType => PostgreSqlSliReportStore.PublishedEvent;
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await using var tx = await transactions.BeginAsync(cancellationToken);
        if (!await inbox.TryRecordProcessedAsync("sli-publications", message.Id, cancellationToken)) return;
        await tx.CommitAsync(cancellationToken);
    }
}
