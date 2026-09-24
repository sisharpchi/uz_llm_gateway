using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.Billing.Infrastructure;

public sealed class PostgreSqlWalletLedgerStore(FoundationDbContext dbContext) : IWalletLedgerStore
{
    public async Task<Wallet?> FindWalletAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        await dbContext.Set<BillingWalletEntity>()
            .AsNoTracking()
            .Where(wallet => wallet.OrganizationId == organizationId)
            .Select(wallet => new Wallet(
                wallet.OrganizationId,
                new UsdMicroAmount(wallet.PostedBalanceMicroUsd),
                new UsdMicroAmount(wallet.ReservedBalanceMicroUsd),
                wallet.Version))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<LedgerPostingStatus> TryAppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Set<BillingWalletEntity>()
                .AsNoTracking()
                .AnyAsync(wallet => wallet.OrganizationId == entry.OrganizationId, cancellationToken))
        {
            return LedgerPostingStatus.WalletNotFound;
        }

        var inserted = await ExecuteInsertAsync(entry, cancellationToken);
        if (inserted == Guid.Empty)
        {
            return LedgerPostingStatus.Duplicate;
        }

        var updated = await ExecuteWalletUpdateAsync(entry, cancellationToken);
        if (updated == 1)
        {
            return LedgerPostingStatus.Posted;
        }

        return LedgerPostingStatus.InsufficientFunds;
    }

    private async Task<Guid> ExecuteInsertAsync(LedgerEntry entry, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            INSERT INTO billing.ledger_entry
                (id, organization_id, type, amount_micro_usd, reference_type, reference_id, metadata_json, occurred_at)
            VALUES
                (@id, @organizationId, @type, @amountMicroUsd, @referenceType, @referenceId, CAST(@metadataJson AS jsonb), @occurredAt)
            ON CONFLICT (organization_id, type, reference_type, reference_id) DO NOTHING
            RETURNING id;
            """);
        AddParameter(command, "id", entry.Id);
        AddParameter(command, "organizationId", entry.OrganizationId);
        AddParameter(command, "type", entry.Type.ToString());
        AddParameter(command, "amountMicroUsd", entry.Amount.Value);
        AddParameter(command, "referenceType", entry.ReferenceType);
        AddParameter(command, "referenceId", entry.ReferenceId);
        AddParameter(command, "metadataJson", entry.MetadataJson);
        AddParameter(command, "occurredAt", entry.OccurredAt);

        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : Guid.Empty;
    }

    private async Task<int> ExecuteWalletUpdateAsync(LedgerEntry entry, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("""
            UPDATE billing.wallet
            SET posted_balance_micro_usd = posted_balance_micro_usd + @amountMicroUsd,
                version = version + 1
            WHERE organization_id = @organizationId
              AND posted_balance_micro_usd + @amountMicroUsd >= reserved_balance_micro_usd;
            """);
        AddParameter(command, "amountMicroUsd", entry.Amount.Value);
        AddParameter(command, "organizationId", entry.OrganizationId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(string commandText)
    {
        var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

public sealed class PostgreSqlPricingHistoryStore(FoundationDbContext dbContext) : IPricingHistoryStore
{
    public async Task AppendFeePolicyVersionAsync(FeePolicyVersion version, CancellationToken cancellationToken = default)
    {
        dbContext.Set<BillingFeePolicyVersionEntity>().Add(new BillingFeePolicyVersionEntity
        {
            Id = version.Id,
            PolicyCode = version.PolicyCode,
            MarkupBasisPoints = version.MarkupBasisPoints,
            FixedFeeMicroUsd = version.FixedFee.Value,
            EffectiveFrom = version.EffectiveFrom,
            EffectiveTo = version.EffectiveTo,
            CreatedAt = version.CreatedAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AppendFxRateSnapshotAsync(FxRateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        dbContext.Set<BillingFxRateSnapshotEntity>().Add(new BillingFxRateSnapshotEntity
        {
            Id = snapshot.Id,
            Source = snapshot.Source,
            UzsTiyinPerUsd = snapshot.UzsTiyinPerUsd,
            ObservedAt = snapshot.ObservedAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
