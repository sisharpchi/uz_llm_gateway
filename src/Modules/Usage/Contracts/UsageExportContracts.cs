namespace UZLLM.Modules.Usage.Contracts;

/// <summary>A complete bounded CSV with the same scope and freshness as its usage read.</summary>
public sealed record UsageCsvExport(byte[] Content, string FileName, int RowCount,
    DateTimeOffset From, DateTimeOffset To, DateTimeOffset DataAsOf,
    string Source, long PendingBucketCount);

/// <summary>Export limits are server-owned; clients cannot request unbounded work.</summary>
public interface IUsageExportService
{
    Task<UsageCsvExport> ExportAsync(Guid organizationId, UsageReadQuery query,
        string? kind = null, int? rowLimit = null, CancellationToken cancellationToken = default);
}

/// <summary>The complete result exceeds an export bound; no partial CSV may be sent.</summary>
public sealed class UsageExportLimitException : Exception
{
    public UsageExportLimitException() : base("Export exceeds its row or byte limit. Narrow the filters.") { }
}
