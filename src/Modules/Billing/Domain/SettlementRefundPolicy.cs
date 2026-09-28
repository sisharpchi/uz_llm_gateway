namespace UZLLM.Modules.Billing.Domain;

public static class SettlementRefundPolicy
{
    public static void EnsureAllowed(string outcome, long chargedMicroUsd,
        long previouslyRefundedMicroUsd, long requestedMicroUsd)
    {
        if (outcome != "Settled" || chargedMicroUsd <= 0)
            throw new InvalidOperationException("Only a settled, positive customer charge may be refunded.");
        if (requestedMicroUsd <= 0 || previouslyRefundedMicroUsd < 0
            || previouslyRefundedMicroUsd > chargedMicroUsd
            || requestedMicroUsd > chargedMicroUsd - previouslyRefundedMicroUsd)
            throw new InvalidOperationException("Refund exceeds the remaining net customer charge.");
    }
}
