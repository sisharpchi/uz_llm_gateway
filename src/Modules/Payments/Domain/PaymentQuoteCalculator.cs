using UZLLM.Modules.Billing.Contracts;

namespace UZLLM.Modules.Payments.Domain;

public static class PaymentQuoteCalculator
{
    public static (UzsTiyinAmount Fee, UsdMicroAmount Credit) Calculate(
        UzsTiyinAmount principal, int feeBasisPoints, UzsTiyinAmount fixedFee,
        decimal uzsTiyinPerUsd)
    {
        if (principal.Value <= 0 || feeBasisPoints is < 0 or > 10_000 || uzsTiyinPerUsd <= 0)
            throw new ArgumentOutOfRangeException(nameof(principal), "A positive amount, valid fee and FX rate are required.");
        var proportional = decimal.Ceiling(principal.Value * (decimal)feeBasisPoints / 10_000m);
        var totalFee = checked((long)proportional + fixedFee.Value);
        if (totalFee >= principal.Value)
            throw new ArgumentException("Payment fee must leave a positive credit principal.", nameof(principal));
        var microUsd = decimal.Floor((principal.Value - totalFee) * 1_000_000m / uzsTiyinPerUsd);
        if (microUsd is < 1 or > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(principal), "The quoted credit is outside the supported range.");
        return (new UzsTiyinAmount(totalFee), new UsdMicroAmount(checked((long)microUsd)));
    }
}
