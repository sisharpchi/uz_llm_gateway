using UZLLM.Modules.Billing.Domain;

namespace UZLLM.Billing.Tests;

public sealed class SettlementRefundPolicyTests
{
    [Fact]
    public void Partial_refund_may_equal_remaining_settled_charge() =>
        SettlementRefundPolicy.EnsureAllowed("Settled", 10_000, 6_000, 4_000);

    [Theory]
    [InlineData("Released", 10_000, 0, 1)]
    [InlineData("Settled", 0, 0, 1)]
    [InlineData("Settled", 10_000, 6_000, 4_001)]
    [InlineData("Settled", 10_000, 0, 0)]
    [InlineData("Settled", 10_000, 10_001, 1)]
    public void Refund_rejects_unsettled_zero_or_excess_charges(string outcome,
        long charged, long alreadyRefunded, long requested) =>
        Assert.Throws<InvalidOperationException>(() =>
            SettlementRefundPolicy.EnsureAllowed(outcome, charged, alreadyRefunded, requested));
}
