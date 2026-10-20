using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api.ReportingEvidence;
using UZLLM.Modules.Billing.Contracts;

namespace UZLLM.Gateway.ContractTests;

public sealed partial class StatementApiTests
{
    [Fact]
    public async Task Evidence_facade_rechecks_session_operator_and_MFA_for_every_call_before_store_access()
    {
        var identity = new Identity(); var store = new EvidenceStore();
        var service = new ReportingEvidenceApiService(identity, store, new EphemeralDataProtectionProvider());
        foreach (var session in new[] { "missing", "session" })
        {
            identity.Operator = session != "session";
            foreach (var action in EvidenceCalls(service, session))
            {
                if (session == "missing") await Assert.ThrowsAsync<ReportingEvidenceSessionException>(action);
                else await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
            }
        }
        identity.Operator = true; identity.Recent = false;
        foreach (var action in EvidenceCalls(service, "session")) await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
        Assert.Equal(0, store.Calls);
        identity.Recent = true; await service.EnsureOperatorAsync("session", default);
        Assert.Null(await service.FindAsync("session", Org, "fiscal", Org, default)); Assert.Equal(1, store.Calls);
    }
    private static IEnumerable<Func<Task>> EvidenceCalls(ReportingEvidenceApiService service, string session)
    {
        yield return () => service.EnsureOperatorAsync(session, default);
        yield return () => service.ListAsync(session, Org, "fiscal", null, null, default);
        yield return () => service.FindAsync(session, Org, "fiscal", Org, default);
        yield return () => service.FiscalAsync(session, Org, null!, default);
        yield return () => service.ContractAsync(session, Org, null!, default);
        yield return () => service.LinkAsync(session, Org, null!, default);
    }
    [Theory]
    [InlineData(0)] [InlineData(101)] [InlineData(-1)]
    public async Task Evidence_list_rejects_invalid_page_bounds_before_store_access(int limit)
    {
        var store = new EvidenceStore(); var service = new ReportingEvidenceApiService(new Identity { Operator = true }, store, new EphemeralDataProtectionProvider());
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("session", Org, "fiscal", limit, null, default)); Assert.Equal(0, store.Calls);
    }
    [Theory]
    [InlineData(8192, 200)] [InlineData(8193, 413)]
    public async Task Evidence_body_cap_applies_to_chunked_case_variant_before_binding(int length, int status)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services); app.UseUzllmReportingEvidenceApiBounds(); var called = false;
        app.Run(_ => { called = true; return Task.CompletedTask; });
        var http = new DefaultHttpContext { RequestServices = services }; http.Request.Method = "POST";
        http.Request.Path = $"/MANAGEMENT/V1/ADMIN/ORGANIZATIONS/{Org}/REPORTING-EVIDENCE/fiscal";
        http.Request.Body = new MemoryStream(new byte[length]); http.Response.Body = new MemoryStream();
        await app.Build()(http); Assert.Equal(status, http.Response.StatusCode); Assert.Equal(status == 200, called);
    }
    [Theory]
    [InlineData("conflict", 409)] [InlineData("profile", 409)] [InlineData("missing", 404)] [InlineData("dependency", 503)] [InlineData("deadline", 504)]
    public async Task Evidence_errors_and_deadlines_are_safe_and_restore_abort_token(string failure, int status)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton(new ReportingEvidenceApiLimits(30)).BuildServiceProvider();
        var app = new ApplicationBuilder(services); app.UseUzllmReportingEvidenceApiBounds();
        app.Run(async http =>
        {
            if (failure == "deadline") await Task.Delay(Timeout.Infinite, http.RequestAborted);
            throw failure switch { "conflict" => new ReportingEvidenceConflictException(), "profile" => new ReportingProfileUnavailableException(),
                "missing" => new KeyNotFoundException(), _ => new InvalidOperationException("SECRET_EXTERNAL_LEGAL_DATA") };
        });
        var http = new DefaultHttpContext { RequestServices = services }; var original = http.RequestAborted;
        http.Request.Path = $"/management/v1/admin/organizations/{Org}/reporting-evidence/fiscal"; http.Response.Body = new MemoryStream();
        await app.Build()(http); Assert.Equal(status, http.Response.StatusCode); Assert.Equal(original, http.RequestAborted);
        http.Response.Body.Position = 0; Assert.DoesNotContain("SECRET_EXTERNAL_LEGAL_DATA", await new StreamReader(http.Response.Body).ReadToEndAsync());
    }
    private sealed class EvidenceStore : IReportingEvidenceStore
    {
        public int Calls;
        public Task<ReportingEvidenceResponse> RegisterFiscalAsync(Guid org, Guid actor, RegisterFiscalEvidenceRequest r, CancellationToken ct)
        { Calls++; throw new NotSupportedException(); }
        public Task<ReportingEvidenceResponse> RegisterContractAsync(Guid org, Guid actor, RegisterContractReferenceRequest r, CancellationToken ct)
        { Calls++; throw new NotSupportedException(); }
        public Task<ReportingEvidenceResponse> LinkAsync(Guid org, Guid actor, LinkContractReportRequest r, CancellationToken ct)
        { Calls++; throw new NotSupportedException(); }
        public Task<ReportingEvidenceResponse?> FindAsync(Guid org, string kind, Guid id, CancellationToken ct)
        { Calls++; return Task.FromResult<ReportingEvidenceResponse?>(null); }
        public Task<IReadOnlyList<ReportingEvidenceResponse>> ListAsync(Guid org, string kind, DateTimeOffset time, Guid id, int take, CancellationToken ct)
        { Calls++; return Task.FromResult<IReadOnlyList<ReportingEvidenceResponse>>([]); }
    }
    [Fact]
    public async Task Evidence_client_abort_propagates_without_success_or_server_timeout_body()
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider(); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var app = new ApplicationBuilder(services); app.UseUzllmReportingEvidenceApiBounds();
        app.Run(http => { http.RequestAborted.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        var http = new DefaultHttpContext { RequestServices = services, RequestAborted = canceled.Token };
        http.Request.Path = $"/management/v1/admin/organizations/{Org}/reporting-evidence/fiscal"; http.Response.Body = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.Build()(http));
        Assert.Equal(canceled.Token, http.RequestAborted); Assert.Equal(0, http.Response.Body.Length); Assert.False(http.Response.HasStarted);
    }
}
