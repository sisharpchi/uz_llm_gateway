using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api.SliReports;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Gateway.ContractTests;

public sealed partial class StatementApiTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("customer")]
    [InlineData("stale")]
    public async Task Sli_facade_rechecks_session_operator_and_recent_MFA_before_every_store_access(string denial)
    {
        var identity = new Identity { Operator = denial != "customer", Recent = denial != "stale" };
        var store = new SliStore(); var facade = SliFacade(identity, store); var session = denial == "missing" ? "gateway-key" : "session";
        var actions = new Func<Task>[] { () => facade.ListAsync(session, "test", null, null, default),
            () => facade.FindAsync(session, Org, default), () => facade.SourcesAsync(session, Org, null, null, default),
            () => facade.ExecutionsAsync(session, Org, null, null, default), () => facade.JobAsync(session, Org, default),
            () => facade.CreateAsync(session, new("test", Month, Org, 0, "Monthly report"), default),
            () => facade.ReviseAsync(session, Org, new(Org, 1, "Explicit revision"), default) };
        foreach (var action in actions)
        {
            if (denial == "missing") await Assert.ThrowsAsync<SliReportSessionRequiredException>(action);
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
        }
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Sli_cursors_are_scope_version_kind_and_page_size_bound_and_preserve_immutable_order()
    {
        var store = new SliStore { Artifact = SliArtifact() }; var facade = SliFacade(store: store);
        var sources = (await facade.SourcesAsync("session", Org, 1, null, default))!;
        Assert.Single(sources.Items); Assert.NotNull(sources.NextCursor);
        var end = (await facade.SourcesAsync("session", Org, 1, sources.NextCursor, default))!;
        Assert.Equal(store.Artifact.Sources[1].Id, Assert.Single(end.Items).Id); Assert.Null(end.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() => facade.SourcesAsync("session", Guid.NewGuid(), 1, sources.NextCursor, default));
        await Assert.ThrowsAsync<ArgumentException>(() => facade.SourcesAsync("session", Org, 2, sources.NextCursor, default));
        await Assert.ThrowsAsync<ArgumentException>(() => facade.ExecutionsAsync("session", Org, 1, sources.NextCursor, default));
        var page = await facade.ListAsync("session", "test", 1, null, default); Assert.NotNull(page.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() => facade.ListAsync("session", "other", 1, page.NextCursor, default));
        await Assert.ThrowsAsync<ArgumentException>(() => facade.ListAsync("session", "test", 1, "tampered", default));
        Assert.Equal(1, store.Artifact.Metadata.Revision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task Sli_page_limits_reject_outside_inclusive_bounds(int limit)
    {
        var facade = SliFacade();
        await Assert.ThrowsAsync<ArgumentException>(() => facade.ListAsync("session", "test", limit, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => facade.SourcesAsync("session", Org, limit, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => facade.ExecutionsAsync("session", Org, limit, null, default));
    }

    [Fact]
    public async Task Sli_terminal_job_state_and_revision_command_never_restart_dead_letter_or_rewrite_predecessor()
    {
        var store = new SliStore { Artifact = SliArtifact() }; var work = new Work {
            Progress = new(Org, 5, 5, Month, null, null, Month) }; var facade = SliFacade(store: store, work: work);
        var result = (await facade.JobAsync("session", Org, default))!;
        Assert.Equal("DeadLettered", result.JobState); Assert.Equal("usage.sli.build", work.Type); Assert.Equal(Org.ToString("N"), work.Key);
        var revised = await facade.ReviseAsync("session", Org, new(Org, 1, "Explicit correction"), default);
        Assert.Equal(Org, store.Command!.PredecessorId); Assert.Equal(1, store.Command.ExpectedGeneration);
        Assert.Equal("test", store.Command.ScopeId); Assert.Equal("DeadLettered", revised!.JobState);
        await Assert.ThrowsAsync<ArgumentException>(() => facade.CreateAsync("session", new("test", Month, Org, 1, "Invalid generation"), default));
        store.Artifact = null; Assert.Null(await facade.ReviseAsync("session", Org, new(Org, 1, "Missing predecessor"), default));
        Assert.Null(await facade.FindAsync("session", Org, default));
    }

    [Theory]
    [InlineData(8192, 200, true)]
    [InlineData(8193, 413, false)]
    public async Task Sli_command_body_bound_applies_before_binding_even_for_chunked_or_case_variant_path(int bytes, int status, bool invoked)
    {
        using var services = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var builder = new ApplicationBuilder(services); builder.UseUzllmSliReportApiBounds(); var called = false;
        builder.Run(_ => { called = true; return Task.CompletedTask; });
        var http = new DefaultHttpContext { RequestServices = services }; http.Request.Method = "POST";
        http.Request.Path = "/management/v1/admin/SLI-REPORTS/"; http.Request.Body = new MemoryStream(new byte[bytes]);
        http.Response.Body = new MemoryStream(); await builder.Build()(http);
        Assert.Equal(status, http.Response.StatusCode); Assert.Equal(invoked, called);
    }

    [Fact]
    public async Task Sli_client_cancellation_is_not_converted_to_a_success_or_dependency_response()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var builder = new ApplicationBuilder(services); builder.UseUzllmSliReportApiBounds(); builder.Run(http => { http.RequestAborted.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        var http = new DefaultHttpContext { RequestServices = services, RequestAborted = canceled.Token };
        http.Request.Path = "/management/v1/admin/sli-reports"; http.Request.Method = "GET"; http.Response.Body = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.Build()(http)); Assert.Equal(0, http.Response.Body.Length); Assert.Equal(canceled.Token, http.RequestAborted);
    }

    [Fact]
    public async Task Sli_worker_job_uses_original_operation_and_attempt_with_forwarded_cancellation()
    {
        var store = new SliStore(); var handler = new SliReportBuildHandler(store); using var cancel = new CancellationTokenSource();
        Assert.Equal("usage.sli.build", handler.JobType);
        await handler.HandleAsync(new(Org, handler.JobType, System.Text.Json.JsonSerializer.Serialize(new { operationId = Org }), Org.ToString("N"), 3, 5), cancel.Token);
        Assert.Equal(1, store.BuildCalls); Assert.Equal(Org, store.BuildId); Assert.Equal(3, store.Attempt); Assert.Equal(cancel.Token, store.Token);
    }

    [Fact]
    public async Task Sli_read_deadline_returns_safe_504_and_restores_original_abort_token()
    {
        using var services = new ServiceCollection().AddLogging().AddRouting().AddSingleton(new SliReportApiLimits(1)).BuildServiceProvider();
        var builder = new ApplicationBuilder(services); builder.UseUzllmSliReportApiBounds(); builder.Run(http => Task.Delay(Timeout.InfiniteTimeSpan, http.RequestAborted));
        var http = new DefaultHttpContext { RequestServices = services }; http.Request.Method = "GET";
        http.Request.Path = "/management/v1/admin/sli-reports"; http.Response.Body = new MemoryStream();
        await builder.Build()(http); Assert.Equal(504, http.Response.StatusCode); Assert.Equal(default, http.RequestAborted);
        http.Response.Body.Position = 0; var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        Assert.Contains("SLI report deadline exceeded", body); Assert.DoesNotContain("Exception", body);
    }

    [Theory]
    [InlineData(null, 21)]
    [InlineData(1, 2)]
    [InlineData(100, 101)]
    public async Task Sli_list_default_and_inclusive_page_bounds_request_only_one_lookahead(int? limit, int take)
    {
        var store = new SliStore(); await SliFacade(store: store).ListAsync("session", "test", limit, null, default);
        Assert.Equal(take, store.Take);
    }

    private static SliReportApiService SliFacade(Identity? identity = null, SliStore? store = null, Work? work = null) =>
        new(identity ?? new(), store ?? new(), new(new Dictionary<string, SliReportScope> { ["test"] = SliArtifact().Scope, ["other"] = SliArtifact().Scope with { Id = "other" } }),
            work ?? new(), new EphemeralDataProtectionProvider(), TimeProvider.System);
    private static SliReportArtifact SliArtifact()
    {
        var ratio = new SliRatio(0, 0, null, "NoData");
        return new(new(Org, "test", Month, Month.AddMonths(1), 1, null, "sli-v1", Month.AddMonths(1), new string('a', 64),
            new(0, 0, 0, 0, 0), ratio, ratio, ratio, "NoData", "NoData", ["MissingSource"], 2, 0),
            new("test", "test", "Chat", "All", [new("edge", "Edge"), new("gateway", "Gateway"), new("probe", "Probe")]),
            Enumerable.Range(0, 2).Select(_ => new SliReportSource(Guid.NewGuid(), "edge", Org, 1, "Edge", "test", 1, 0, 0, 0, Month, Month, Month, false, false, new string('a', 64), 0, false)).ToArray(), [], []);
    }
    private sealed class SliStore : ISliReportStore
    {
        public int Calls; public int Take; public SliReportArtifact? Artifact; public SliReportCommand? Command;
        public int BuildCalls; public Guid BuildId; public int Attempt; public CancellationToken Token;
        public Task<SliReportOperation> RequestAsync(SliReportCommand command, CancellationToken ct = default)
        { Calls++; Command = command; return Task.FromResult(new SliReportOperation(Org, command.ScopeId, command.PeriodStart, "Pending", command.ExpectedGeneration, null, null)); }
        public Task BuildAsync(Guid id, int attempt, CancellationToken ct = default) { BuildCalls++; BuildId = id; Attempt = attempt; Token = ct; return Task.CompletedTask; }
        public Task<SliReportOperation?> OperationAsync(Guid id, CancellationToken ct = default) { Calls++; return Task.FromResult<SliReportOperation?>(new(Org, "test", Month, "Pending", 0, null, null)); }
        public Task<SliReportArtifact?> FindAsync(Guid id, CancellationToken ct = default) { Calls++; return Task.FromResult(Artifact); }
        public Task<IReadOnlyList<SliReportMetadata>> ListAsync(string scope, DateTimeOffset before, long revision, int take, CancellationToken ct = default)
        { Calls++; Take = take; return Task.FromResult<IReadOnlyList<SliReportMetadata>>([SliArtifact().Metadata, SliArtifact().Metadata with { Revision = 2 }]); }
    }
}
