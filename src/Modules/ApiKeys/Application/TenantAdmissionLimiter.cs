using System.Data.Common;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.ApiKeys.Domain;

namespace UZLLM.Modules.ApiKeys.Application;

public sealed class TenantAdmissionLimiter(ITenantLimitStore store, IDistributedAdmissionLimiter inner)
    : IDistributedAdmissionLimiter
{
    public async Task<LimitDecision> TryAcquireAsync(LimitScope scope, LimitPolicy policy, Guid requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await store.FindAsync(scope, cancellationToken);
            return snapshot is null ? new(LimitOutcome.DependencyUnavailable, null, TimeSpan.FromSeconds(1))
                : await inner.TryAcquireAsync(scope, TenantLimitPolicy.Apply(snapshot, policy), requestId, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or TimeoutException
            || exception is InvalidOperationException { InnerException: DbException or TimeoutException })
        {
            return new(LimitOutcome.DependencyUnavailable, null, TimeSpan.FromSeconds(1));
        }
    }

    // Finalization must release the lease even while the policy database is unavailable.
    public Task<LimitReleaseOutcome> ReleaseAsync(LimitLease lease, CancellationToken cancellationToken = default) =>
        inner.ReleaseAsync(lease, cancellationToken);
}
