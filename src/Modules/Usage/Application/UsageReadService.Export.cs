using System.Globalization;
using System.Text;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Modules.Usage.Application;

public sealed partial class UsageReadService
{
    public const int MaximumExportRows = 5000;
    public const int MaximumExportBytes = 4 * 1024 * 1024;

    public async Task<UsageCsvExport> ExportAsync(Guid organizationId, UsageReadQuery query,
        string? kind = null, int? rowLimit = null, CancellationToken cancellationToken = default)
    {
        var filter = Normalize(organizationId, query);
        var format = kind?.ToLowerInvariant() ?? "activity";
        if (format is not ("activity" or "daily"))
            throw new ArgumentException("Export kind must be activity or daily.", nameof(kind));
        var limit = rowLimit ?? 1000;
        if (limit is < 1 or > MaximumExportRows)
            throw new ArgumentException("Export rowLimit must be between 1 and 5000.", nameof(rowLimit));
        cancellationToken.ThrowIfCancellationRequested();

        using var output = new MemoryStream();
        output.Write(Encoding.UTF8.GetPreamble());
        DateTimeOffset asOf;
        string source;
        long pending;
        int rowCount;
        string[] metadata;
        if (format == "activity")
        {
            // A single bounded statement, not an unbounded loop over HTTP pages.
            var rows = await store.ListActivityAsync(filter, null, limit + 1, cancellationToken);
            if (rows.Count > limit) throw new UsageExportLimitException();
            asOf = timeProvider.GetUtcNow();
            source = "AuthoritativeRequest";
            pending = 0;
            rowCount = rows.Count;
            metadata = Metadata();
            WriteRow(["requestId", "projectId", "projectName", "apiKeyId", "apiKeyName",
                "modelId", "modelCode", "selectedModelId", "selectedModelCode", "providerId", "providerCode",
                "startedAt", "completedAt", "executionState", "deliveryState", "financialState",
                "httpStatus", "isStream", "attemptCount", "durationMs", "inputTokens", "outputTokens",
                "chargedMicroUsd", .. MetadataColumns]);
            foreach (var row in rows)
                WriteRow([row.RequestId.ToString("D"), row.ProjectId.ToString("D"), row.ProjectName,
                    row.ApiKeyId.ToString("D"), row.ApiKeyName, row.ModelId.ToString("D"), row.ModelCode,
                    row.SelectedModelId?.ToString("D"), row.SelectedModelCode, row.ProviderId?.ToString("D"), row.ProviderCode,
                    Utc(row.StartedAt), row.CompletedAt is { } completed ? Utc(completed) : null,
                    row.ExecutionState, row.DeliveryState, row.FinancialState, Number(row.HttpStatus),
                    row.IsStream ? "true" : "false", Number(row.AttemptCount), Number(row.DurationMs),
                    Number(row.InputTokens), Number(row.OutputTokens), row.ChargedMicroUsd, .. metadata]);
        }
        else
        {
            // Exactly the same snapshot, financial fields and lag semantics as /timeseries.
            var series = await store.ReadTimeSeriesAsync(filter, cancellationToken);
            if (series.Items.Count > limit) throw new UsageExportLimitException();
            asOf = series.DataAsOf;
            source = series.Aggregation.Source;
            pending = series.Aggregation.PendingBucketCount;
            rowCount = series.Items.Count;
            metadata = Metadata();
            WriteRow(["day", "requestCount", "errorCount", "inputTokens", "outputTokens",
                "chargedMicroUsd", "refundedMicroUsd", "netChargedMicroUsd", .. MetadataColumns]);
            foreach (var row in series.Items)
                WriteRow([row.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Number(row.RequestCount),
                    Number(row.ErrorCount), Number(row.InputTokens), Number(row.OutputTokens), row.ChargedMicroUsd,
                    row.RefundedMicroUsd, row.NetChargedMicroUsd, .. metadata]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new UsageCsvExport(output.ToArray(), $"uzllm-{format}.csv", rowCount,
            filter.From, filter.To, asOf, source, pending);

        string[] Metadata() => [Utc(filter.From), Utc(filter.To), Utc(asOf), source, Number(pending)!];
        void WriteRow(string?[] cells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(',', cells.Select(QuoteCell)) + "\r\n";
            var length = Encoding.UTF8.GetByteCount(line);
            if (output.Length + length > MaximumExportBytes) throw new UsageExportLimitException();
            output.Write(Encoding.UTF8.GetBytes(line));
        }
    }

    private static readonly string[] MetadataColumns =
        ["fromUtc", "toUtc", "dataAsOf", "source", "pendingBucketCount"];

    private static string Utc(DateTimeOffset value) => value.ToUniversalTime()
        .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    private static string? Number(long? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string QuoteCell(string? value)
    {
        value ??= "";
        // CSV quoting alone does not stop formulas. Check controls and whitespace bypasses,
        // including full-width formula prefixes. See the documented spreadsheet caveat.
        var first = value.AsSpan();
        while (!first.IsEmpty && (char.IsWhiteSpace(first[0]) || char.IsControl(first[0])
            || char.GetUnicodeCategory(first[0]) == UnicodeCategory.Format)) first = first[1..];
        if (value.Length > 0 && (char.IsControl(value[0])
                || char.GetUnicodeCategory(value[0]) == UnicodeCategory.Format)
            || !first.IsEmpty && "=+-@＝＋－＠".Contains(first[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
