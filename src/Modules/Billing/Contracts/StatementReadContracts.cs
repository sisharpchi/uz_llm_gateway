namespace UZLLM.Modules.Billing.Contracts;

/// <summary>Frozen UTC posting month; not request-period analytics.</summary>
public sealed record StatementPeriodResponse(DateTimeOffset From, DateTimeOffset To);
/// <summary>Exact signed integer USD micro-units, never floating-point currency.</summary>
public sealed record StatementTotalsResponse(string OpeningMicroUsd, string MovementMicroUsd, string ClosingMicroUsd,
    string PostPeriodMovementMicroUsd, string GrossUsageMicroUsd, string RefundedUsageMicroUsd, string NetUsageMicroUsd);
/// <summary>Snapshot balances; holds and debt are not posted consumption.</summary>
public sealed record StatementBalanceResponse(string PostedMicroUsd, string HeldMicroUsd, string AvailableMicroUsd, string RecoveryDebtMicroUsd);
/// <summary>Change relative to the explicit predecessor, not a rewritten statement.</summary>
public sealed record StatementDeltaResponse(string ClosingMicroUsd, int Added, int Removed, int Changed);
/// <summary>Customer-safe immutable version metadata. No internal snapshot, source payload or operator provenance.</summary>
public sealed record StatementMetadataResponse(int SchemaVersion, string CalculationVersion, Guid Id, string Kind,
    string Label, Guid OrganizationId, StatementPeriodResponse Period, long Revision, Guid? PredecessorId,
    DateTimeOffset SourceAsOf, string SourceDigest, string FinancialCoverage, StatementPending Pending,
    StatementTotalsResponse Totals, StatementBalanceResponse Balance, StatementDeltaResponse Delta,
    int LineCount, DateTimeOffset PublishedAt)
{
    public long Generation => Revision;
    public string MoneyUnit => "USD micro-units";
}
/// <summary>One allowlisted immutable wallet posting; no raw metadata or operator reason.</summary>
public sealed record StatementLineResponse(int Ordinal, Guid Id, string Category, DateTimeOffset OccurredAt,
    string AmountMicroUsd, string ReferenceType, Guid ReferenceId);
/// <summary>Version-pinned CSV bytes and their publication digest.</summary>
public sealed record StatementCsv(byte[] Content, string Sha256);
/// <summary>Trusted internal read port. Authorization belongs in the Management application facade.</summary>
public interface IStatementReadStore
{
    Task<bool> OrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatementMetadataResponse>> ListAsync(Guid organizationId, DateTimeOffset from, DateTimeOffset to,
        DateTimeOffset beforePeriod, long beforeRevision, int take, CancellationToken cancellationToken);
    Task<StatementMetadataResponse?> FindAsync(Guid organizationId, Guid versionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatementLineResponse>> LinesAsync(Guid organizationId, Guid versionId, int afterOrdinal,
        int take, CancellationToken cancellationToken);
    Task<StatementCsv?> CsvAsync(Guid organizationId, Guid versionId, CancellationToken cancellationToken);
}
