using Microsoft.EntityFrameworkCore;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

public sealed class PostgreSqlFinancialStore(FoundationDbContext db) : IFinancialStore
{
    public async Task<AdmissionStatus> TryReserveAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        var validScope = await db.Set<UsageRequestEntity>().AsNoTracking()
            .Where(request => request.Id == reservation.RequestId
                && request.OrganizationId == reservation.OrganizationId
                && request.ProjectId == reservation.ProjectId
                && request.ApiKeyId == reservation.ApiKeyId)
            .Join(db.Set<OrganizationEntity>(), request => request.OrganizationId, org => org.Id,
                (request, org) => new { request, org })
            .Join(db.Set<ProjectEntity>(), pair => pair.request.ProjectId, project => project.Id,
                (pair, project) => new { pair.request, pair.org, project })
            .Join(db.Set<GatewayApiKeyEntity>(), pair => pair.request.ApiKeyId, key => key.Id,
                (pair, key) => new { pair.org, pair.project, key })
            .AnyAsync(pair => pair.org.Status == "Active" && pair.project.Status == "Active"
                && pair.key.Status == "Active"
                && (pair.key.ExpiresAt == null || pair.key.ExpiresAt > reservation.CreatedAt), cancellationToken);
        if (!validScope) return AdmissionStatus.InvalidScope;

        var validFee = await db.Set<BillingFeePolicyVersionEntity>().AsNoTracking()
            .AnyAsync(value => value.Id == reservation.FeePolicyVersionId
                && value.EffectiveFrom <= reservation.CreatedAt
                && (value.EffectiveTo == null || reservation.CreatedAt < value.EffectiveTo), cancellationToken);
        if (!validFee) return AdmissionStatus.InvalidFeePolicy;

        var walletUpdated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE billing.wallet AS wallet
            SET reserved_balance_micro_usd = wallet.reserved_balance_micro_usd + {reservation.Amount.Value},
                version = wallet.version + 1
            WHERE wallet.organization_id = {reservation.OrganizationId}
              AND wallet.posted_balance_micro_usd - wallet.reserved_balance_micro_usd >= {reservation.Amount.Value}
              AND NOT EXISTS (
                  SELECT 1 FROM billing.recovery_debt AS debt
                  WHERE debt.organization_id = wallet.organization_id AND debt.spending_held);
            """, cancellationToken);
        if (walletUpdated != 1)
        {
            var held = await db.Set<BillingRecoveryDebtEntity>().AsNoTracking()
                .AnyAsync(value => value.OrganizationId == reservation.OrganizationId && value.SpendingHeld,
                    cancellationToken);
            return held ? AdmissionStatus.SpendingHeld : AdmissionStatus.InsufficientWallet;
        }

        var policies = await db.Set<BillingBudgetPolicyEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == reservation.OrganizationId
                && value.ProjectId == reservation.ProjectId
                && (value.ApiKeyId == null || value.ApiKeyId == reservation.ApiKeyId))
            .OrderBy(value => value.ApiKeyId == null ? 0 : 1).ThenBy(value => value.Id)
            .ToListAsync(cancellationToken);
        foreach (var policy in policies)
        {
            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE billing.budget_bucket AS bucket
                SET reserved_micro_usd = bucket.reserved_micro_usd + {reservation.Amount.Value}
                WHERE bucket.policy_id = {policy.Id}
                  AND bucket.captured_micro_usd + bucket.reserved_micro_usd + {reservation.Amount.Value} <= {policy.LimitMicroUsd};
                """, cancellationToken);
            if (updated != 1)
                return policy.ApiKeyId is null ? AdmissionStatus.ProjectBudgetExceeded
                    : AdmissionStatus.ApiKeyBudgetExceeded;
        }

        db.Set<BillingReservationEntity>().Add(new BillingReservationEntity
        {
            Id = reservation.Id, RequestId = reservation.RequestId,
            OrganizationId = reservation.OrganizationId, ProjectId = reservation.ProjectId,
            ApiKeyId = reservation.ApiKeyId, FeePolicyVersionId = reservation.FeePolicyVersionId,
            AmountMicroUsd = reservation.Amount.Value, Status = "Reserved",
            CreatedAt = reservation.CreatedAt, ExpiresAt = reservation.ExpiresAt
        });
        foreach (var policy in policies)
            db.Set<BillingReservationBudgetEntity>().Add(new BillingReservationBudgetEntity
            {
                ReservationId = reservation.Id, PolicyId = policy.Id,
                AmountMicroUsd = reservation.Amount.Value
            });
        await db.SaveChangesAsync(cancellationToken);
        await db.Set<UsageRequestEntity>().Where(value => value.Id == reservation.RequestId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.FinancialState, "Reserved"), cancellationToken);
        return AdmissionStatus.Reserved;
    }

    public async Task<FinalizationContext?> LockAndLoadAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        var requestId = await db.Set<BillingReservationEntity>().AsNoTracking()
            .Where(value => value.Id == reservationId).Select(value => (Guid?)value.RequestId)
            .SingleOrDefaultAsync(cancellationToken);
        if (requestId is null) return null;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE usage.request SET financial_state = financial_state WHERE id = {requestId.Value};",
            cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.reservation SET status = status WHERE id = {reservationId};",
            cancellationToken);
        var entity = await db.Set<BillingReservationEntity>().AsNoTracking()
            .SingleAsync(value => value.Id == reservationId, cancellationToken);
        var reservation = ToContract(entity);
        var fee = await db.Set<BillingFeePolicyVersionEntity>().AsNoTracking()
            .SingleAsync(value => value.Id == entity.FeePolicyVersionId, cancellationToken);
        var feePolicy = new FeePolicyVersion(fee.Id, fee.PolicyCode, fee.MarkupBasisPoints,
            new UsdMicroAmount(fee.FixedFeeMicroUsd), fee.EffectiveFrom, fee.EffectiveTo, fee.CreatedAt);
        var prior = await db.Set<BillingSettlementEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.ReservationId == reservationId, cancellationToken);
        var attempts = await db.Set<UsageAttemptEntity>().AsNoTracking()
            .Where(value => value.RequestId == requestId.Value).ToListAsync(cancellationToken);
        var evidenceEntities = await db.Set<UsageEvidenceEntity>().AsNoTracking()
            .Where(value => value.RequestId == requestId.Value).ToListAsync(cancellationToken);
        var priceIds = evidenceEntities.Where(value => value.State == "Verified")
            .Select(value => value.PriceVersionId!.Value).Distinct().ToArray();
        var prices = await db.Set<CatalogModelPriceEntity>().AsNoTracking()
            .Where(value => priceIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
        var evidence = evidenceEntities.Select(value => new UsageEvidence(value.Id, value.RequestId,
            value.AttemptId, value.ProviderModelId, Enum.Parse<EvidenceState>(value.State),
            Enum.Parse<EvidenceSource>(value.Source), value.InputTokens, value.OutputTokens,
            value.CachedInputTokens, value.ReasoningTokens, value.PriceVersionId,
            value.ProviderRequestId, value.CapturedAt, value.ReconcileAfter)).ToArray();
        var priced = evidenceEntities.Where(value => value.State == "Verified")
            .Select(value =>
            {
                var price = prices[value.PriceVersionId!.Value];
                var attempt = attempts.Single(candidate => candidate.Id == value.AttemptId);
                return new PricedUsageEvidence(value.Id, value.AttemptId, value.InputTokens!.Value,
                    value.OutputTokens!.Value, value.CachedInputTokens!.Value, value.ReasoningTokens,
                    price.InputPriceMicroUsdPerMillion, price.OutputPriceMicroUsdPerMillion,
                    price.CachedInputPriceMicroUsdPerMillion, price.ExtraPricingJson,
                    attempt.StartedAt, price.EffectiveFrom, price.EffectiveTo);
            }).ToArray();
        var attemptContracts = attempts.Select(value => new FinancialAttempt(value.Id,
            Enum.Parse<ExecutionState>(value.ExecutionState),
            evidenceEntities.Any(item => item.AttemptId == value.Id))).ToArray();
        return new FinalizationContext(reservation, feePolicy, prior is null ? null : ToContract(prior),
            attemptContracts, evidence, priced);
    }

    public Task<Guid?> FindReservationIdAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        db.Set<BillingReservationEntity>().AsNoTracking().Where(value => value.RequestId == requestId)
            .Select(value => (Guid?)value.Id).SingleOrDefaultAsync(cancellationToken);

    public Task<Guid?> FindReservationIdByEvidenceAsync(Guid evidenceId, CancellationToken cancellationToken = default) =>
        db.Set<UsageEvidenceEntity>().AsNoTracking().Where(value => value.Id == evidenceId)
            .Join(db.Set<BillingReservationEntity>(), evidence => evidence.RequestId,
                reservation => reservation.RequestId, (_, reservation) => (Guid?)reservation.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TryRecordLateExposureAsync(Guid settlementId, Guid evidenceId,
        UsdMicroAmount providerCost, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var alreadyAccounted = await db.Set<BillingSettlementEvidenceEntity>().AsNoTracking()
            .AnyAsync(value => value.EvidenceId == evidenceId, cancellationToken);
        if (alreadyAccounted) return false;
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO billing.platform_exposure (id, settlement_id, evidence_id, provider_cost_micro_usd, created_at)
            VALUES ({Guid.CreateVersion7()}, {settlementId}, {evidenceId}, {providerCost.Value}, {now})
            ON CONFLICT (evidence_id) DO NOTHING;
            """, cancellationToken);
        return inserted == 1;
    }

    public async Task<Settlement> ApplyFinalizationAsync(FinalizationContext context, ChargeBreakdown charge,
        bool unresolvedUsage, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var reservation = context.Reservation;
        if (charge.Charged.Value > reservation.Amount.Value)
            throw new InvalidOperationException("A charge cannot exceed its reservation.");
        var outcome = charge.Charged.Value > 0 ? "Settled" : "Released";
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE billing.reservation SET status = {outcome}, captured_micro_usd = {charge.Charged.Value},
                finalized_at = {now}
            WHERE id = {reservation.Id} AND status = 'Reserved';
            """, cancellationToken);
        if (updated != 1) throw new InvalidOperationException("The reservation has already reached a terminal state.");
        updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE billing.wallet SET posted_balance_micro_usd = posted_balance_micro_usd - {charge.Charged.Value},
                reserved_balance_micro_usd = reserved_balance_micro_usd - {reservation.Amount.Value},
                version = version + 1
            WHERE organization_id = {reservation.OrganizationId}
              AND reserved_balance_micro_usd >= {reservation.Amount.Value}
              AND posted_balance_micro_usd >= {charge.Charged.Value};
            """, cancellationToken);
        if (updated != 1) throw new InvalidOperationException("Wallet reservation and settlement are inconsistent.");

        var holds = await db.Set<BillingReservationBudgetEntity>().AsNoTracking()
            .Where(value => value.ReservationId == reservation.Id).OrderBy(value => value.PolicyId)
            .ToListAsync(cancellationToken);
        foreach (var hold in holds)
        {
            updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE billing.budget_bucket SET reserved_micro_usd = reserved_micro_usd - {hold.AmountMicroUsd},
                    captured_micro_usd = captured_micro_usd + {charge.Charged.Value}
                WHERE policy_id = {hold.PolicyId} AND reserved_micro_usd >= {hold.AmountMicroUsd};
                """, cancellationToken);
            if (updated != 1) throw new InvalidOperationException("Budget reservation and settlement are inconsistent.");
        }

        var settlement = new Settlement(Guid.CreateVersion7(), reservation.Id, reservation.RequestId,
            charge.ProviderCost, charge.UncappedCustomerCharge, charge.Charged,
            charge.UncollectedCharge, charge.PlatformExposure, unresolvedUsage, outcome, now);
        db.Set<BillingSettlementEntity>().Add(new BillingSettlementEntity
        {
            Id = settlement.Id, ReservationId = reservation.Id, RequestId = reservation.RequestId,
            OrganizationId = reservation.OrganizationId,
            ProviderCostMicroUsd = charge.ProviderCost.Value,
            UncappedCustomerChargeMicroUsd = charge.UncappedCustomerCharge.Value,
            ChargedMicroUsd = charge.Charged.Value,
            UncollectedChargeMicroUsd = charge.UncollectedCharge.Value,
            PlatformExposureMicroUsd = charge.PlatformExposure.Value,
            UnresolvedUsage = unresolvedUsage, Outcome = outcome, CreatedAt = now
        });
        foreach (var evidence in context.Evidence)
            db.Set<BillingSettlementEvidenceEntity>().Add(new BillingSettlementEvidenceEntity
            {
                SettlementId = settlement.Id, EvidenceId = evidence.Id
            });
        if (charge.Charged.Value > 0)
            db.Set<BillingLedgerEntryEntity>().Add(new BillingLedgerEntryEntity
            {
                Id = Guid.CreateVersion7(), OrganizationId = reservation.OrganizationId,
                Type = LedgerEntryType.UsageCharge.ToString(), AmountMicroUsd = -charge.Charged.Value,
                ReferenceType = "reservation", ReferenceId = reservation.Id,
                MetadataJson = "{}", OccurredAt = now
            });
        await db.Set<UsageRequestEntity>().Where(value => value.Id == reservation.RequestId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.FinancialState, outcome), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await RecoverAvailableDebtAsync(reservation.OrganizationId, settlement.Id, now, cancellationToken);
        return settlement;
    }

    public async Task<BudgetPolicy?> SetBudgetAsync(Guid organizationId, Guid projectId, Guid? apiKeyId,
        UsdMicroAmount limit, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.wallet SET version = version WHERE organization_id = {organizationId};",
            cancellationToken) != 1) return null;
        var validProject = await db.Set<ProjectEntity>().AsNoTracking().AnyAsync(value =>
            value.Id == projectId && value.OrganizationId == organizationId, cancellationToken);
        var validKey = apiKeyId is null || await db.Set<GatewayApiKeyEntity>().AsNoTracking()
            .AnyAsync(value => value.Id == apiKeyId && value.ProjectId == projectId, cancellationToken);
        if (!validProject || !validKey) return null;
        var existing = await db.Set<BillingBudgetPolicyEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.ProjectId == projectId && value.ApiKeyId == apiKeyId,
                cancellationToken);
        if (existing is null)
        {
            // Installing a new all-time cap while holds are active would require
            // retroactively attaching those holds. Fail closed and retry later.
            var hasActiveReservations = await db.Set<BillingReservationEntity>().AsNoTracking()
                .AnyAsync(value => value.ProjectId == projectId
                    && (apiKeyId == null || value.ApiKeyId == apiKeyId)
                    && value.Status == "Reserved", cancellationToken);
            if (hasActiveReservations) return null;
            var captured = await db.Set<BillingSettlementEntity>().AsNoTracking()
                .Join(db.Set<BillingReservationEntity>(), settlement => settlement.ReservationId,
                    reservation => reservation.Id, (settlement, reservation) => new { settlement, reservation })
                .Where(value => value.reservation.ProjectId == projectId
                    && (apiKeyId == null || value.reservation.ApiKeyId == apiKeyId))
                .SumAsync(value => (long?)value.settlement.ChargedMicroUsd, cancellationToken) ?? 0;
            var policy = new BillingBudgetPolicyEntity
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, ProjectId = projectId,
                ApiKeyId = apiKeyId, LimitMicroUsd = limit.Value, CreatedAt = now, UpdatedAt = now
            };
            db.Set<BillingBudgetPolicyEntity>().Add(policy);
            db.Set<BillingBudgetBucketEntity>().Add(new BillingBudgetBucketEntity
            {
                PolicyId = policy.Id, CapturedMicroUsd = captured
            });
            await db.SaveChangesAsync(cancellationToken);
            return new BudgetPolicy(policy.Id, organizationId, projectId, apiKeyId, limit,
                new UsdMicroAmount(captured), UsdMicroAmount.Zero);
        }
        var bucket = await db.Set<BillingBudgetBucketEntity>().AsNoTracking()
            .SingleAsync(value => value.PolicyId == existing.Id, cancellationToken);
        await db.Set<BillingBudgetPolicyEntity>().Where(value => value.Id == existing.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.LimitMicroUsd, limit.Value)
                .SetProperty(value => value.UpdatedAt, now), cancellationToken);
        return new BudgetPolicy(existing.Id, organizationId, projectId, apiKeyId, limit,
            new UsdMicroAmount(bucket.CapturedMicroUsd), new UsdMicroAmount(bucket.ReservedMicroUsd));
    }

    public async Task<ReversalResult> ApplyConfirmedReversalAsync(Guid organizationId, Guid externalReferenceId,
        UsdMicroAmount amount, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing.wallet SET version = version WHERE organization_id = {organizationId};",
            cancellationToken) != 1) throw new KeyNotFoundException("The organization wallet does not exist.");
        var prior = await db.Set<BillingReversalEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId
                && value.ExternalReferenceId == externalReferenceId, cancellationToken);
        if (prior is not null)
        {
            if (prior.AmountMicroUsd != amount.Value)
                throw new InvalidOperationException("A reversal reference cannot be reused with a different amount.");
            return new ReversalResult(prior.Id, organizationId, externalReferenceId, amount,
                new UsdMicroAmount(prior.RecoveredMicroUsd), new UsdMicroAmount(prior.DebtCreatedMicroUsd), true);
        }
        var wallet = await db.Set<BillingWalletEntity>().AsNoTracking()
            .SingleAsync(value => value.OrganizationId == organizationId, cancellationToken);
        var recovered = Math.Min(amount.Value, wallet.PostedBalanceMicroUsd - wallet.ReservedBalanceMicroUsd);
        var debtCreated = amount.Value - recovered;
        if (recovered > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE billing.wallet SET posted_balance_micro_usd = posted_balance_micro_usd - {recovered},
                    version = version + 1 WHERE organization_id = {organizationId};
                """, cancellationToken);
            db.Set<BillingLedgerEntryEntity>().Add(new BillingLedgerEntryEntity
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId,
                Type = LedgerEntryType.AdjustmentDebit.ToString(), AmountMicroUsd = -recovered,
                ReferenceType = "payment_reversal", ReferenceId = externalReferenceId,
                MetadataJson = "{}", OccurredAt = now
            });
        }
        if (debtCreated > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO billing.recovery_debt (organization_id, outstanding_micro_usd, spending_held, version)
                VALUES ({organizationId}, {debtCreated}, TRUE, 1)
                ON CONFLICT (organization_id) DO UPDATE SET
                    outstanding_micro_usd = billing.recovery_debt.outstanding_micro_usd + {debtCreated},
                    spending_held = TRUE, version = billing.recovery_debt.version + 1;
                """, cancellationToken);
            db.Set<BillingDebtEntryEntity>().Add(new BillingDebtEntryEntity
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId,
                Type = "Incurred", AmountMicroUsd = debtCreated,
                ReferenceId = externalReferenceId, CreatedAt = now
            });
        }
        var reversal = new BillingReversalEntity
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId,
            ExternalReferenceId = externalReferenceId, AmountMicroUsd = amount.Value,
            RecoveredMicroUsd = recovered, DebtCreatedMicroUsd = debtCreated, CreatedAt = now
        };
        db.Set<BillingReversalEntity>().Add(reversal);
        await db.SaveChangesAsync(cancellationToken);
        return new ReversalResult(reversal.Id, organizationId, externalReferenceId, amount,
            new UsdMicroAmount(recovered), new UsdMicroAmount(debtCreated), false);
    }

    public async Task<FinancialWalletState?> FindWalletStateAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var wallet = await db.Set<BillingWalletEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId, cancellationToken);
        if (wallet is null) return null;
        var debt = await db.Set<BillingRecoveryDebtEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId, cancellationToken);
        return new FinancialWalletState(new Wallet(organizationId,
            new UsdMicroAmount(wallet.PostedBalanceMicroUsd),
            new UsdMicroAmount(wallet.ReservedBalanceMicroUsd), wallet.Version),
            new UsdMicroAmount(debt?.OutstandingMicroUsd ?? 0), debt?.SpendingHeld ?? false);
    }

    public async Task RecoverAvailableDebtAsync(Guid organizationId, Guid referenceId,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var debt = await db.Set<BillingRecoveryDebtEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrganizationId == organizationId, cancellationToken);
        if (debt is null || debt.OutstandingMicroUsd == 0) return;
        var wallet = await db.Set<BillingWalletEntity>().AsNoTracking()
            .SingleAsync(value => value.OrganizationId == organizationId, cancellationToken);
        var recovered = Math.Min(debt.OutstandingMicroUsd,
            wallet.PostedBalanceMicroUsd - wallet.ReservedBalanceMicroUsd);
        if (recovered <= 0) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE billing.wallet SET posted_balance_micro_usd = posted_balance_micro_usd - {recovered},
                version = version + 1 WHERE organization_id = {organizationId};
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE billing.recovery_debt SET outstanding_micro_usd = outstanding_micro_usd - {recovered},
                spending_held = (outstanding_micro_usd - {recovered} > 0), version = version + 1
            WHERE organization_id = {organizationId};
            """, cancellationToken);
        db.Set<BillingLedgerEntryEntity>().Add(new BillingLedgerEntryEntity
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId,
            Type = LedgerEntryType.AdjustmentDebit.ToString(), AmountMicroUsd = -recovered,
            ReferenceType = "debt_recovery", ReferenceId = referenceId,
            MetadataJson = "{}", OccurredAt = now
        });
        db.Set<BillingDebtEntryEntity>().Add(new BillingDebtEntryEntity
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId,
            Type = "Recovered", AmountMicroUsd = -recovered,
            ReferenceId = referenceId, CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Reservation ToContract(BillingReservationEntity value) => new(value.Id, value.RequestId,
        value.OrganizationId, value.ProjectId, value.ApiKeyId, value.FeePolicyVersionId,
        new UsdMicroAmount(value.AmountMicroUsd), value.CreatedAt, value.ExpiresAt, value.Status);

    private static Settlement ToContract(BillingSettlementEntity value) => new(value.Id,
        value.ReservationId, value.RequestId, new UsdMicroAmount(value.ProviderCostMicroUsd),
        new UsdMicroAmount(value.UncappedCustomerChargeMicroUsd), new UsdMicroAmount(value.ChargedMicroUsd),
        new UsdMicroAmount(value.UncollectedChargeMicroUsd), new UsdMicroAmount(value.PlatformExposureMicroUsd),
        value.UnresolvedUsage, value.Outcome, value.CreatedAt);
}
