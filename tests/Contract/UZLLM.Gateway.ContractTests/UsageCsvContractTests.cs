using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using UZLLM.Modules.Usage.Application;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed class UsageCsvContractTests
{
    private static readonly Guid OrganizationId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1:A2)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(A1)")]
    [InlineData("\t=1")]
    [InlineData("\r=1")]
    [InlineData("\n=1")]
    [InlineData("  =1")]
    [InlineData("\u2003+1")]
    [InlineData("\uFEFF=1")]
    [InlineData(" \u200B=1")]
    [InlineData("\0=1")]
    [InlineData("＝1")]
    [InlineData("＋1")]
    [InlineData("－1")]
    [InlineData("＠1")]
    [InlineData("=1+2\";,=HYPERLINK(\"bad\")\r\nmore")]
    public async Task Csv_escapes_formula_prefixes_and_preserves_quoted_cell_boundaries(string name)
    {
        var store = new ExportStore { Rows = [Row() with { ProjectName = name, ApiKeyName = "Safe, \"quoted\"\r\nToshkent" }] };
        var result = await Service(store).ExportAsync(OrganizationId, new());
        var row = Assert.Single(Parse(result.Content));
        Assert.Equal("'" + name, row["projectName"]);
        Assert.Equal("Safe, \"quoted\"\r\nToshkent", row["apiKeyName"]);
        Assert.Equal(28, row.Count);
        Assert.StartsWith("\uFEFF\"requestId\"", Encoding.UTF8.GetString(result.Content));
        Assert.Equal(name, store.Rows[0].ProjectName); // Sanitization never changes source data.
    }

    [Fact]
    public async Task Activity_csv_preserves_unknowns_utc_and_integer_money_without_coercion()
    {
        var first = Row() with { ProjectName = "O‘zbekiston", ChargedMicroUsd = "9007199254740993", InputTokens = 7 };
        var store = new ExportStore { Rows = [first, Row() with { RequestId = Guid.NewGuid(), ChargedMicroUsd = null }] };
        var query = new UsageReadQuery(From: Now.AddHours(-1).ToOffset(TimeSpan.FromHours(5)), To: Now.ToOffset(TimeSpan.FromHours(5)));
        var csv = await Service(store).ExportAsync(OrganizationId, query);
        var rows = Parse(csv.Content);
        Assert.Equal("9007199254740993", rows[0]["chargedMicroUsd"]);
        Assert.Equal("7", rows[0]["inputTokens"]);
        Assert.Equal("O‘zbekiston", rows[0]["projectName"]);
        Assert.Equal("", rows[1]["chargedMicroUsd"]);
        Assert.Equal("", rows[1]["inputTokens"]);
        Assert.Equal("", rows[1]["outputTokens"]);
        Assert.Equal("", rows[1]["providerId"]);
        Assert.Equal("", rows[1]["completedAt"]);
        Assert.EndsWith("Z", rows[0]["startedAt"]);
        Assert.Equal(Now.AddHours(-1), DateTimeOffset.Parse(rows[0]["fromUtc"], CultureInfo.InvariantCulture));
        Assert.Equal("AuthoritativeRequest", csv.Source);
        Assert.Equal("uzllm-activity.csv", csv.FileName);
        Assert.Equal(1001, store.Take);
        Assert.Null(store.Cursor);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5000)]
    public async Task Export_accepts_exact_row_bound_but_rejects_one_extra_without_truncation(int limit)
    {
        var store = new ExportStore { Rows = Enumerable.Repeat(Row(), limit).ToArray() };
        Assert.Equal(limit, (await Service(store).ExportAsync(OrganizationId, new(), rowLimit: limit)).RowCount);
        Assert.Equal(limit + 1, store.Take);
        store.Rows = Enumerable.Repeat(Row(), limit + 1).ToArray();
        await Assert.ThrowsAsync<UsageExportLimitException>(() => Service(store).ExportAsync(OrganizationId, new(), rowLimit: limit));
    }

    [Fact]
    public async Task Export_counts_utf8_bytes_accepts_exact_byte_bound_and_rejects_one_extra()
    {
        var store = new ExportStore { Rows = [Row() with { ProjectName = "" }] };
        var baseline = await Service(store).ExportAsync(OrganizationId, new());
        var remaining = UsageReadService.MaximumExportBytes - baseline.Content.Length;
        var padding = new string('é', remaining / 2) + new string('x', remaining % 2);
        store.Rows = [Row() with { ProjectName = padding }];
        var exact = await Service(store).ExportAsync(OrganizationId, new());
        Assert.Equal(UsageReadService.MaximumExportBytes, exact.Content.Length);
        store.Rows = [Row() with { ProjectName = padding + "x" }];
        await Assert.ThrowsAsync<UsageExportLimitException>(() => Service(store).ExportAsync(OrganizationId, new()));
    }

    [Theory]
    [InlineData("activity")]
    [InlineData("daily")]
    public async Task Empty_export_has_headers_and_no_fabricated_data_or_totals(string kind)
    {
        var csv = await Service(new ExportStore { Rows = [] }).ExportAsync(OrganizationId, new(), kind);
        Assert.Equal(0, csv.RowCount);
        Assert.Empty(Parse(csv.Content));
        Assert.Single(Encoding.UTF8.GetString(csv.Content).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Daily_export_preserves_rollup_lag_refund_and_net_fields()
    {
        var store = new ExportStore { Daily = [new(DateOnly.FromDateTime(Now.Date), 3, 1, 12, 7, "4300", "1000", "3300")] };
        var csv = await Service(store).ExportAsync(OrganizationId, new(), "daily");
        var row = Assert.Single(Parse(csv.Content));
        Assert.Equal("2026-10-11", row["day"]);
        Assert.Equal("3", row["requestCount"]);
        Assert.Equal("4300", row["chargedMicroUsd"]);
        Assert.Equal("1000", row["refundedMicroUsd"]);
        Assert.Equal("3300", row["netChargedMicroUsd"]);
        Assert.Equal("2", row["pendingBucketCount"]);
        Assert.Equal(DateTimeOffset.UnixEpoch, csv.DataAsOf);
        Assert.Equal("HourlyRollupsWithLiveEdges", csv.Source);
        Assert.Equal("uzllm-daily.csv", csv.FileName);
        Assert.Equal(0, store.Take);
        store.Daily = [.. store.Daily, .. store.Daily];
        await Assert.ThrowsAsync<UsageExportLimitException>(() => Service(store).ExportAsync(OrganizationId, new(), "daily", 1));
    }

    [Theory]
    [InlineData("summary", 1)]
    [InlineData("", 1)]
    [InlineData("activity", 0)]
    [InlineData("daily", 5001)]
    public async Task Invalid_export_kind_or_bound_is_rejected_before_store_access(string kind, int limit)
    {
        var store = new ExportStore();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(store).ExportAsync(OrganizationId, new(), kind, limit));
        Assert.Null(store.Filter);
    }

    [Fact]
    public async Task Export_preserves_cancellation_and_never_masks_store_failure_as_empty_csv()
    {
        var store = new ExportStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(store).ExportAsync(OrganizationId, new(), cancellationToken: cancellation.Token));
        Assert.Null(store.Filter);
        store.Failure = new InvalidOperationException("database unavailable");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store).ExportAsync(OrganizationId, new()));
    }

    private static UsageReadService Service(ExportStore store) => new(store, new Clock());
    private static UsageActivityItem Row() => new(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
        Guid.NewGuid(), "Project", Guid.NewGuid(), "Key", Guid.NewGuid(), "test-chat", null, null,
        Now.AddMinutes(-1), null, "OutcomeUnknown", "Partial", "PendingEvidence", null, true, 1, null, null, null, null);

    internal static List<Dictionary<string, string>> Parse(byte[] content)
    {
        using var parser = new TextFieldParser(new StringReader(Encoding.UTF8.GetString(content).TrimStart('\uFEFF')))
            { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields()!;
        var rows = new List<Dictionary<string, string>>();
        while (!parser.EndOfData)
        {
            var values = parser.ReadFields()!;
            Assert.Equal(headers.Length, values.Length);
            rows.Add(headers.Zip(values).ToDictionary(value => value.First, value => value.Second));
        }
        return rows;
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    internal sealed class ExportStore : IUsageReadStore
    {
        public IReadOnlyList<UsageActivityItem> Rows { get; set; } = [Row()];
        public IReadOnlyList<UsageDailyBucket> Daily { get; set; } = [];
        public UsageReadFilter? Filter { get; private set; }
        public UsageActivityCursor? Cursor { get; private set; }
        public int Take { get; private set; }
        public Exception? Failure { get; set; }
        public Task<IReadOnlyList<UsageActivityItem>> ListActivityAsync(UsageReadFilter filter, UsageActivityCursor? cursor, int take, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Filter = filter; Cursor = cursor; Take = take;
            if (Failure is { } failure) throw failure;
            return Task.FromResult(Rows);
        }
        public Task<UsageTimeSeries> ReadTimeSeriesAsync(UsageReadFilter filter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Filter = filter;
            return Task.FromResult(new UsageTimeSeries(filter.From, filter.To, DateTimeOffset.UnixEpoch, Daily, new(2, Now)));
        }
        public Task<UsageRequestDetail?> FindDetailAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UsageSummary> ReadSummaryAsync(UsageReadFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UsageBreakdown> ReadBreakdownAsync(UsageReadFilter filter, UsageBreakdownDimension dimension, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
