namespace UZLLM.Modules.Payments.Contracts;

public sealed record PaymentConfiguration(
    int? FeeBasisPoints,
    long? FixedFeeTiyin,
    string? PaymeMerchantId,
    string? PaymeKey,
    string? ClickMerchantId,
    string? ClickServiceId,
    string? ClickSecretKey)
{
    public string MerchantScope(PaymentProvider provider) => provider switch
    {
        PaymentProvider.Payme when !string.IsNullOrWhiteSpace(PaymeMerchantId) => $"payme:{PaymeMerchantId}",
        PaymentProvider.Click when !string.IsNullOrWhiteSpace(ClickServiceId) => $"click:{ClickServiceId}",
        _ => throw new InvalidOperationException($"{provider} merchant identity is not configured.")
    };
}
