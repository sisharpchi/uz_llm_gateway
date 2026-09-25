using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Payments.Application;
using UZLLM.Modules.Payments.Contracts;

namespace UZLLM.Modules.Payments.Infrastructure;

public static class PaymentServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments");
        services.AddSingleton(new PaymentConfiguration(
            section.GetValue<int?>("FeeBasisPoints"),
            section.GetValue<long?>("FixedFeeTiyin"),
            section["Payme:MerchantId"], section["Payme:Key"],
            section["Click:MerchantId"], section["Click:ServiceId"], section["Click:SecretKey"]));
        services.AddScoped<IPaymentStore, PostgreSqlPaymentStore>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<PaymeMerchantApi>();
        services.AddScoped<ClickShopApi>();
        return services;
    }
}
