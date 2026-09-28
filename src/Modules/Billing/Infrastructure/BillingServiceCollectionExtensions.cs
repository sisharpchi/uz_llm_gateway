using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Billing.Application;
using UZLLM.Modules.Billing.Contracts;

namespace UZLLM.Modules.Billing.Infrastructure;

public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmBilling(this IServiceCollection services)
    {
        services.AddScoped<IWalletLedgerStore, PostgreSqlWalletLedgerStore>();
        services.AddScoped<IPricingHistoryStore, PostgreSqlPricingHistoryStore>();
        services.AddScoped<ISettlementRefundStore, PostgreSqlSettlementRefundStore>();
        services.AddScoped<IFinancialStore, PostgreSqlFinancialStore>();
        services.AddScoped<IWalletLedgerService, WalletLedgerService>();
        services.AddScoped<IPricingHistoryService, PricingHistoryService>();
        services.AddScoped<IFinancialService, FinancialService>();
        services.AddScoped<IFinancialRecoverySweep, FinancialRecoverySweep>();
        return services;
    }
}
