using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Billing.Application;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;

namespace UZLLM.Modules.Billing.Infrastructure;

public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmBilling(this IServiceCollection services)
    {
        services.AddScoped<IWalletLedgerStore, PostgreSqlWalletLedgerStore>();
        services.AddScoped<IPricingHistoryStore, PostgreSqlPricingHistoryStore>();
        services.AddScoped<ISettlementRefundStore, PostgreSqlSettlementRefundStore>();
        services.AddScoped<IWalletAdjustmentStore, PostgreSqlWalletAdjustmentStore>();
        services.AddScoped<IFinancialStore, PostgreSqlFinancialStore>();
        services.AddScoped<IWalletLedgerService, WalletLedgerService>();
        services.AddScoped<IPricingHistoryService, PricingHistoryService>();
        services.AddScoped<IFinancialService, FinancialService>();
        services.AddScoped<IFinancialRecoverySweep, FinancialRecoverySweep>();
        return services;
    }

    // Opt-in only in a composition root that also registers the Payments evidence port.
    // Gateway admission does not load reporting or depend on Payments.
    public static IServiceCollection AddUzllmStatementSources(this IServiceCollection services)
    {
        services.AddScoped<IStatementSourceReader, PostgreSqlStatementSourceReader>();
        return services;
    }

    public static IServiceCollection AddUzllmStatementPublication(this IServiceCollection services)
    {
        services.AddUzllmStatementSources();
        services.AddSingleton(new StatementBuildLimits());
        services.AddScoped<IStatementArtifactBuilder, StatementArtifactBuilder>();
        services.AddScoped<IStatementPublicationStore, PostgreSqlStatementPublicationStore>();
        services.AddScoped<UZLLM.Persistence.ILeasedJobHandler, StatementBuildJobHandler>();
        services.AddScoped<UZLLM.Persistence.IOutboxHandler, StatementPublishedEventHandler>();
        return services;
    }
}
