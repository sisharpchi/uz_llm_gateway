using System.Text.Json;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Application;

public sealed class FinancialRecoverySweep(IFinancialStore store, IFinancialService financial,
    ILeasedJobStore jobs, TimeProvider clock) : IFinancialRecoverySweep
{
    public async Task<RecoverySweepPage> SweepPageAsync(RecoveryCursor? after, int limit,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var due = await store.ListDueRecoveryAsync(now, after, limit, cancellationToken);
        foreach (var candidate in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await financial.ReconcileAsync(candidate.ReservationId, cancellationToken);
                if (result.Status == FinalizationStatus.PendingEvidence
                    && now >= candidate.ExpiresAt.AddHours(24))
                    await EscalateAsync(candidate.ReservationId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { throw; }
            catch
            {
                // The alert job is deduplicated by reservation. If scheduling
                // itself fails, the Worker retries this page on its next pass.
                await EscalateAsync(candidate.ReservationId, cancellationToken);
            }
        }
        var last = due.LastOrDefault();
        return new RecoverySweepPage(due.Count,
            last is null ? null : new RecoveryCursor(last.ExpiresAt, last.ReservationId));
    }

    private Task<Guid> EscalateAsync(Guid reservationId, CancellationToken cancellationToken) =>
        jobs.ScheduleAsync("billing.recovery.alert", JsonSerializer.Serialize(new { reservationId }),
            reservationId.ToString("N"), clock.GetUtcNow(), cancellationToken: cancellationToken);
}
