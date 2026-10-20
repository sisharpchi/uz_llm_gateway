using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Gateway.Api.Inference;
using UZLLM.Modules.Payments.Contracts;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Usage.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Management.Api.Statements;
using UZLLM.Management.Api.SliReports;
using UZLLM.Management.Api.ReportingEvidence;

namespace UZLLM.Gateway.ContractTests;

public sealed class OpenApiWireContractTests : IDisposable
{
    private readonly ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string Root => FindRoot();

    [Fact]
    public async Task External_evidence_wire_keeps_operator_only_exact_units_and_no_issuance_or_entitlement_overrides()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Root, "docs/api/management-v1.openapi.json")));
        var paths = document.RootElement.GetProperty("paths"); const string root = "/management/v1/admin/organizations/{organizationId}/reporting-evidence";
        foreach (var (path, method) in new[] { (root + "/fiscal", "post"), (root + "/contracts", "post"), (root + "/links", "post"),
            (root + "/{kind}", "get"), (root + "/{kind}/{id}", "get") })
        {
            var op = paths.GetProperty(path).GetProperty(method); Assert.Contains("recent MFA", op.GetProperty("description").GetString());
            Assert.Contains(op.GetProperty("security").EnumerateArray(), s => s.TryGetProperty("Session", out _));
            foreach (var status in new[] { "401", "403", "503", "504" }) Assert.True(op.GetProperty("responses").TryGetProperty(status, out _));
            var success = op.GetProperty("responses").GetProperty(method == "post" ? "201" : "200");
            Assert.Equal("no-store", success.GetProperty("headers").GetProperty("Cache-Control").GetProperty("schema").GetProperty("enum")[0].GetString());
            if (method == "post")
            {
                Assert.True(success.GetProperty("headers").TryGetProperty("Location", out _));
                Assert.Contains(op.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == IdentityCookieNames.CsrfHeader && p.GetProperty("required").GetBoolean());
            }
        }
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "RegisterFiscalEvidenceRequest", "RegisterContractReferenceRequest", "LinkContractReportRequest" })
        {
            var schema = schemas.GetProperty(name); Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            foreach (var forbidden in new[] { "approved", "taxRate", "archiveUrl", "eligibility", "creditAmount" }) Assert.False(schema.GetProperty("properties").TryGetProperty(forbidden, out _));
        }
        Assert.Equal("EvidenceOnly", schemas.GetProperty("ReportingEvidenceResponse").GetProperty("properties").GetProperty("eligibility").GetProperty("enum")[0].GetString());
        Assert.Equal("string", schemas.GetProperty("FiscalEvidenceAmounts").GetProperty("properties").GetProperty("totalUnits").GetProperty("type").GetString());
        Assert.False(paths.TryGetProperty("/management/v1/invoices/issue", out _));
    }

    [Fact]
    public async Task Sli_wire_is_additive_operator_only_with_no_exclusion_overrides_and_explicit_job_errors()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Root, "docs/api/management-v1.openapi.json")));
        var paths = document.RootElement.GetProperty("paths");
        const string root = "/management/v1/admin/sli-reports";
        foreach (var (path, method) in new[] { (root, "get"), (root, "post"), (root + "/{id}", "get"),
            (root + "/{id}/sources", "get"), (root + "/{id}/executions", "get"), (root + "/{id}/revisions", "post"), (root + "/jobs/{id}", "get") })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            Assert.Contains("recent MFA", operation.GetProperty("description").GetString());
            Assert.Contains(operation.GetProperty("security").EnumerateArray(), value => value.TryGetProperty("Session", out _));
            foreach (var status in new[] { "401", "403", "503", "504" }) Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
            if (method == "post")
            {
                Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), value => value.GetProperty("name").GetString() == IdentityCookieNames.CsrfHeader && value.GetProperty("required").GetBoolean());
                Assert.True(operation.GetProperty("responses").GetProperty("202").GetProperty("headers").TryGetProperty("Location", out _));
            }
        }
        var create = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateSliReportRequest");
        Assert.False(create.GetProperty("additionalProperties").GetBoolean());
        Assert.False(create.GetProperty("properties").TryGetProperty("maintenanceExclusion", out _));
        Assert.Equal(0, create.GetProperty("properties").GetProperty("expectedGeneration").GetProperty("maximum").GetInt32());
        Assert.False(paths.TryGetProperty("/management/v1/sli-reports", out _));
    }

    [Fact]
    public async Task Provider_policy_wire_documents_unknown_provenance_and_operator_security()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(Root, "docs/api/management-v1.openapi.json")));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var policy = schemas.GetProperty("ProviderDataPolicyResponse").GetProperty("properties");
        Assert.Equal(new[] { "Unknown", "MayRetain", "NoRetentionReported" },
            policy.GetProperty("retention").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(new[] { "Unknown", "MayTrain", "NoTrainingReported" },
            policy.GetProperty("training").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.False(policy.TryGetProperty("zdr", out _));
        Assert.False(policy.TryGetProperty("publishedByAccountId", out _));
        foreach (var name in new[] { "versionId", "sourceUrl", "sourceObservedAt", "publishedAt", "scope", "conditions" })
            Assert.Contains(policy.GetProperty(name).GetProperty("type").EnumerateArray(), value => value.GetString() == "null");
        Assert.Equal(16, policy.GetProperty("regions").GetProperty("maxItems").GetInt32());
        var paths = document.RootElement.GetProperty("paths");
        var publication = paths.GetProperty("/management/v1/admin/providers/{providerId}/data-policies").GetProperty("post");
        Assert.Contains("recent MFA", publication.GetProperty("description").GetString());
        Assert.Contains(publication.GetProperty("parameters").EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == IdentityCookieNames.CsrfHeader
                && parameter.GetProperty("required").GetBoolean());
        foreach (var status in new[] { "201", "400", "401", "403", "404", "409" })
            Assert.True(publication.GetProperty("responses").TryGetProperty(status, out _));
        var customer = paths.GetProperty("/management/v1/organizations/{organizationId}/projects/{projectId}/catalog/providers/{providerCode}/data-policy")
            .GetProperty("get");
        Assert.Contains(customer.GetProperty("security").EnumerateArray(), security => security.TryGetProperty("Session", out _));
    }

    [Fact]
    public async Task Published_examples_use_real_parser_writer_and_management_serialization()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(Root, "docs/api/gateway-v1.openapi.json")));
        var example = document.RootElement.GetProperty("paths").GetProperty("/v1/chat/completions")
            .GetProperty("post").GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("example");
        var parsed = ChatRequestParser.Parse(Encoding.UTF8.GetBytes(example.GetRawText()));
        Assert.Equal("example-chat", parsed.Model);
        Assert.Equal(32, parsed.ProviderRequest.MaxOutputTokens);
        Assert.False(parsed.Stream);

        var id = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var now = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var snapshots = new List<WireExample>();
        var embeddingInput = document.RootElement.GetProperty("paths").GetProperty("/v1/embeddings")
            .GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
        var embeddingParsed = EmbeddingsRequestParser.Parse(Encoding.UTF8.GetBytes(embeddingInput.GetRawText()));
        Assert.Equal(2, embeddingParsed.EmbeddingRequest!.Inputs.Count);
        foreach (var known in new[] { true, false })
        {
            var embeddingContext = Context();
            await OpenAiEmbeddingsWriter.WriteAsync(embeddingContext, new ProviderEmbeddingResult(
                [new(0, [0.25f, -0.5f]), new(1, [-0.5f, 0.25f])], known ? new(5, 0, 0, null) : null, "upstream"),
                embeddingParsed.Model, CancellationToken.None);
            snapshots.Add(new("gateway", "/v1/embeddings", "post", 200, "application/json", Body(embeddingContext)));
        }
        var context = Context();
        await new OpenAiCompletionWriter(context).WriteCompletionAsync(new ProviderCompletion(
            "upstream", "upstream-model", new ProviderMessage("assistant",
                [new(ProviderContentKind.Text, "Hello")], [new("call_1", "lookup", "{\"q\":\"test\"}")]),
            "tool_calls", new ProviderUsage(12, 3, 2, 1), "upstream-request"), parsed.Model, id, CancellationToken.None);
        var completion = Body(context);
        Assert.Equal(15, completion.GetProperty("usage").GetProperty("total_tokens").GetInt32());
        Assert.Equal("lookup", completion.GetProperty("choices")[0].GetProperty("message")
            .GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
        snapshots.Add(new("gateway", "/v1/chat/completions", "post", 200, "application/json", FixedTimestamp(completion)));

        context = Context();
        await new OpenAiCompletionWriter(context).WriteCompletionAsync(new ProviderCompletion(
            "upstream", "upstream-model", new ProviderMessage("assistant", [new(ProviderContentKind.Text, "Hello")]),
            "stop", null, null), parsed.Model, id, CancellationToken.None);
        Assert.Equal(JsonValueKind.Null, Body(context).GetProperty("usage").ValueKind);
        snapshots.Add(new("gateway", "/v1/chat/completions", "post", 200, "application/json", FixedTimestamp(Body(context))));

        var responsesExample = document.RootElement.GetProperty("paths").GetProperty("/v1/responses")
            .GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
        var responseRequest = ResponsesRequestParser.Parse(Encoding.UTF8.GetBytes(responsesExample.GetRawText()));
        Assert.Equal("responses", responseRequest.Operation);
        Assert.Equal(32, responseRequest.ProviderRequest.MaxOutputTokens);
        foreach (var finish in new[] { "stop", "length", "content_filter", "unknown-usage" })
        {
            context = Context();
            await new OpenAiResponsesWriter(context, responseRequest).WriteCompletionAsync(new("u", "m",
                new("assistant", [new(ProviderContentKind.Text, "Hello")]), finish == "unknown-usage" ? "stop" : finish,
                finish == "unknown-usage" ? null : new(12, 5, 4, 2), "provider-id",
                finish == "content_filter" ? "Synthetic refusal" : null), "example-chat", id, CancellationToken.None);
            snapshots.Add(new("gateway", "/v1/responses", "post", 200, "application/json", FixedTimestamp(Body(context))));
        }

        foreach (var finish in new[] { "stop", "length", "content_filter", "unknown-usage", "failure" })
        {
            context = Context();
            var writer = new OpenAiResponsesWriter(context, responseRequest);
            await writer.WriteStreamEventAsync(new(ProviderStreamKind.TextDelta, Text: "Hello"), "example-chat", id, CancellationToken.None);
            if (finish == "content_filter") await writer.WriteStreamEventAsync(new(ProviderStreamKind.Refusal, Text: "Synthetic refusal"), "example-chat", id, CancellationToken.None);
            await writer.WriteStreamEventAsync(new(ProviderStreamKind.Finish, FinishReason: finish is "unknown-usage" or "failure" ? "stop" : finish), "example-chat", id, CancellationToken.None);
            if (finish != "unknown-usage") await writer.WriteStreamEventAsync(new(ProviderStreamKind.Usage, Usage: new(12, 5, 4, 2)), "example-chat", id, CancellationToken.None);
            if (finish == "failure") await writer.WriteStreamErrorAsync("Finalization is pending reconciliation.", "finalization_pending", CancellationToken.None);
            else await writer.FinishStreamAsync(CancellationToken.None);
            var frames = GatewayExecutionTests.ResponsesEvents(Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
            var sse = string.Concat(frames.Select(frame => {
                var node = System.Text.Json.Nodes.JsonNode.Parse(frame.GetRawText())!.AsObject();
                if (node["response"] is { } response) response["created_at"] = 1790812800;
                return "event: " + node["type"]!.GetValue<string>() + "\ndata: " + node.ToJsonString() + "\n\n";
            }));
            snapshots.Add(new("gateway", "/v1/responses", "post", 200, "text/event-stream", JsonSerializer.SerializeToElement(sse)));
        }

        foreach (var status in new[] { 400, 401, 402, 403, 409, 413, 415, 429, 502, 503 })
        {
            context = Context();
            await GatewayErrorResponse.WriteAsync(context, new(status, "example_error", "server_error",
                "Synthetic contract rejection", status == 409 ? id : null), CancellationToken.None);
            Assert.Equal(status, context.Response.StatusCode);
            Assert.Equal(JsonValueKind.Null, Body(context).GetProperty("error").GetProperty("param").ValueKind);
            if (status == 409) Assert.Equal(id.ToString("N"), context.Response.Headers["X-Original-Request-Id"]);
            snapshots.Add(new("gateway", "/v1/chat/completions", "post", status, "application/json", Body(context)));
            snapshots.Add(new("gateway", "/v1/responses", "post", status, "application/json", Body(context)));
        }

        const string org = "/management/v1/organizations/{organizationId}";
        var statement = new StatementMetadataResponse(1, "account-statement-v1", id, "AccountStatement",
            "Not a tax invoice / No payment due from this statement", id, new(now, now.AddMonths(1)), 1, null,
            now.AddMonths(1), new string('a', 64), "ReconciledSnapshot", new(1, 2, 3),
            new("9007199254740993", "-5", "9007199254740988", "0", "5", "0", "5"),
            new("9007199254740988", "10", "9007199254740978", "0"), new("0", 4, 0, 0), 1, now.AddMonths(1));
        Add(org + "/statements", "get", 200, new StatementPage([statement], null));
        Add(org + "/statements/{id}", "get", 200, statement);
        Add(org + "/statements/{id}/lines", "get", 200, new StatementLinePage(id,
            [new(1, id, "UsageCharge", now, "-5", "reservation", id)], null));
        var reportJob = new StatementJobResponse(id, id, "Pending", 0, null, null, "Queued",
            new(id, 0, 5, now, null, null, null));
        const string reports = "/management/v1/admin/organizations/{organizationId}/reports";
        Add(reports + "/statements", "post", 202, reportJob);
        Add(reports + "/statements/{id}/revisions", "post", 202, reportJob with { ExpectedGeneration = 1 });
        Add(reports + "/jobs/{id}", "get", 200, reportJob with { JobState = "DeadLettered",
            Job = reportJob.Job! with { AttemptCount = 5, DeadLetteredAt = now.AddMinutes(1) } });
        const string sliPath = "/management/v1/admin/sli-reports";
        var sliRatio = new SliRatio(0, 0, null, "NoData");
        var sliMetadata = new SliReportMetadata(id, "example-chat", now, now.AddMonths(1), 1, null, "sli-v1", now.AddMonths(1),
            new string('a', 64), new(0, 0, 0, 0, 0), sliRatio, sliRatio, sliRatio, "NoData", "NoData", ["MissingSource"], 0, 0);
        var sliScope = new SliReportScope("example-chat", "test", "Chat", "All",
            [new("edge-a", "Edge"), new("gateway-a", "Gateway"), new("probe-a", "Probe")]);
        var sliJob = new SliReportJobResponse(new(id, "example-chat", now, "Pending", 0, null, null), "Queued", reportJob.Job);
        Add(sliPath, "get", 200, new SliReportPage([sliMetadata], null));
        Add(sliPath + "/{id}", "get", 200, new SliReportResponse(sliMetadata, sliScope, []));
        Add(sliPath + "/{id}/sources", "get", 200, new SliSourcePage(id, [], null));
        Add(sliPath + "/{id}/executions", "get", 200, new SliExecutionPage(id, [], null));
        Add(sliPath, "post", 202, sliJob); Add(sliPath + "/{id}/revisions", "post", 202, sliJob);
        Add(sliPath + "/jobs/{id}", "get", 200, sliJob with { JobState = "DeadLettered" });
        const string evidencePath = "/management/v1/admin/organizations/{organizationId}/reporting-evidence";
        var evidenceProfile = new ReportingEvidenceProfile("synthetic-evidence", 1, true, "test-issuer", "test-jurisdiction", "test-series",
            "TEST", 3, "Invoice", "test-issuer-snapshot", "test-tax-policy", "test-correction-policy", "test-archive-policy", now, now.AddYears(1),
            [.. new[] { "Finance", "Legal", "Security", "Operations" }.Select(role => new ReportingApproval(role, "synthetic-reviewer", "synthetic-approval", now))],
            [new("example-chat", "test", "Chat", "All")]);
        var fiscalCommand = new RegisterFiscalEvidenceRequest("synthetic-evidence", 1, id, "test-external-operation", "Registered", "test-external-process",
            "Synthetic external observation", new("TEST-001", now, "Invoice", "test-customer-snapshot", new("9007199254740993", null, null),
                new string('a', 64), "test-controlled-archive", "test-issuer-verification"));
        var fiscalEvidence = new ReportingEvidenceResponse(id, id, "fiscal", "Registered", now, id, null, evidenceProfile, fiscalCommand,
            null, null, null, null, null, null);
        Add(evidencePath + "/fiscal", "post", 201, fiscalEvidence);
        var contractCommand = new RegisterContractReferenceRequest("synthetic-evidence", 1, id, "test-contract", "v1", "example-chat", "test-customer-snapshot",
            now, now.AddYears(1), "EvidenceOnly", "Synthetic contract provenance");
        Add(evidencePath + "/contracts", "post", 201, fiscalEvidence with { Kind = "contracts", State = "EvidenceOnly", Fiscal = null,
            Contract = contractCommand, Scope = evidenceProfile.Scopes[0] });
        Add(evidencePath + "/links", "post", 201, fiscalEvidence with { Kind = "links", State = "EvidenceOnly", Fiscal = null,
            Link = new(id, id, now, "Synthetic report linkage", id), Scope = evidenceProfile.Scopes[0], StatementSourceDigest = new string('b', 64) });
        Add(evidencePath + "/{kind}", "get", 200, new ReportingEvidencePage([fiscalEvidence], null));
        Add(evidencePath + "/{kind}/{id}", "get", 200, fiscalEvidence);
        snapshots.Add(new("management", org + "/statements/{id}/export", "get", 200, "text/csv",
            JsonSerializer.SerializeToElement("\"Not a tax invoice / No payment due from this statement\"\r\nordinal,ledger_id,category,posted_at,amount_micro_usd\r\n")));
        var unknownPolicy = UZLLM.Management.Api.Catalog.ProviderDataPolicyResponse.From(id, null);
        var reportedPolicy = UZLLM.Management.Api.Catalog.ProviderDataPolicyResponse.From(id, new(
            id, id, "MayRetain", "NoTrainingReported", "ProviderReported", ["US"], "Synthetic public API scope",
            "Conditional provider report, not a ZDR guarantee.", "https://example.com/provider-policy", now.AddDays(-1), now, id));
        foreach (var policy in new[] { unknownPolicy, reportedPolicy })
            Add(org + "/projects/{projectId}/catalog/providers/{providerCode}/data-policy", "get", 200, policy);
        var adminPolicy = new UZLLM.Management.Api.Administration.AdminProviderDataPolicyResponse(reportedPolicy, id);
        Add("/management/v1/admin/providers/{providerId}/data-policies", "post", 201, adminPolicy);
        Add("/management/v1/admin/providers/{providerId}/data-policies", "get", 200, new[] { adminPolicy });
        Add("/management/v1/admin/provider-data-policies/{id}", "get", 200, adminPolicy);
        var pendingCredit = new UZLLM.Management.Api.Administration.AdminWalletAdjustmentResponse(id, id, id,
            "example-credit-key", "9007199254740993", "Documented service credit", now, "Pending", null, false);
        var approvedCredit = pendingCredit with { Status = "Approved", Approval = new(id,
            Guid.Parse("22222222-2222-4222-8222-222222222222"), "Independent credit approval", now.AddMinutes(1)) };
        Add("/management/v1/admin/wallet-adjustments", "post", 201, pendingCredit);
        Add("/management/v1/admin/wallet-adjustments", "post", 200, approvedCredit with { Duplicate = true });
        Add("/management/v1/admin/wallet-adjustments", "get", 200, new[] { pendingCredit, approvedCredit });
        Add("/management/v1/admin/wallet-adjustments/{id}", "get", 200, approvedCredit);
        Add("/management/v1/admin/wallet-adjustments/{id}/approve", "post", 200, approvedCredit);
        Add("/management/v1/admin/wallet-adjustments/{id}/approve", "post", 200, approvedCredit with { Duplicate = true });
        Add(org + "/alerts/events", "get", 200, new[] {
            new UZLLM.Modules.Notifications.Infrastructure.AlertEventView(id, id, "LowBalance", null, 100,
                "Delivered", now, now.AddSeconds(1)) });
        Add(org + "/projects/{projectId}/settings", "get", 200, new UZLLM.Modules.Projects.Contracts.ProjectSettings());
        Add(org + "/projects/{projectId}/settings", "put", 200,
            new UZLLM.Modules.Projects.Contracts.ProjectSettings("price", "Production", ["example-chat"], ["openai"]));
        Add("/management/v1/auth/session", "get", 200,
            new UZLLM.Modules.Identity.Infrastructure.IdentityServiceCollectionExtensions.SessionResponse(id, "example@example.test", true, false));
        Add("/management/v1/projects/{projectId}/api-keys", "get", 200,
            new[] { new UZLLM.Modules.ApiKeys.Contracts.GatewayApiKey(id, id, "Restricted", "123456abcdef",
                UZLLM.Modules.ApiKeys.Contracts.GatewayApiKeyStatus.Active, null, id, now,
                Permissions: new(AllowedModels: ["example-chat"], DeniedProviders: ["anthropic"])),
                new UZLLM.Modules.ApiKeys.Contracts.GatewayApiKey(id, id, "Unrestricted", "abcdef123456",
                    UZLLM.Modules.ApiKeys.Contracts.GatewayApiKeyStatus.Active, null, id, now) });
        Add(org + "/billing/wallet", "get", 200, new WalletResponse(id,
            "9007199254740993", "100", "9007199254740893", "0", false, 3));
        Add(org + "/provider-keys/providers", "get", 200,
            new[] { new ByokProviderOption(id, "google", "Gemini") });
        foreach (var resource in new[] { "projects/{projectId}", "api-keys/{apiKeyId}" })
        {
            var path = "/management/v1/" + resource + "/limits";
            Add(path, "get", 200, new UZLLM.Modules.ApiKeys.Contracts.TenantLimitView(new(), new(60, 8), new(60, 8)));
            Add(path, "put", 200, new UZLLM.Modules.ApiKeys.Contracts.TenantLimitView(new(12, 2), new(12, 2), new(60, 8)));
        }
        var intent = new PaymentIntentResponse(id, id, "Click", id, "Pending", "10000000", "10000",
            "7500000", id, "133200000", null, now, null, null, null);
        Add(org + "/billing/quotes", "post", 200, new PaymentQuoteResponse(id, id, "Click",
            "10000000", "10000", "7500000", id, "133200000", now, now.AddMinutes(10)));
        Add(org + "/billing/topups", "post", 201, new CreatedTopUpResponse(intent, false, "https://example.test/checkout"));
        Add(org + "/billing/topups", "post", 200, new CreatedTopUpResponse(intent, true, "https://example.test/checkout"));
        Add(org + "/billing/topups/{intentId}", "get", 200, intent);
        Add(org + "/usage/summary", "get", 200, new UsageSummary(now.AddDays(-1), now, now,
            1, 1, 0, 0, 10, 5, "3200", 0, [], [], "1000", "2200",
            new UsageAggregationInfo(1, now, "HourlyRollupsWithLiveEdges")));

        context = Context();
        await Results.Problem(title: "Unable to update payload retention.", statusCode: 500).ExecuteAsync(context);
        snapshots.Add(new("management", org + "/projects/{projectId}/payload-retention", "put", 500,
            "application/problem+json", Body(context)));
        context = Context();
        await Results.Problem("Synthetic dependency failure", statusCode: 503).ExecuteAsync(context);
        snapshots.Add(new("management", org + "/billing/quotes", "post", 503, "application/problem+json", Body(context)));
        context = Context();
        await Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Required"] }).ExecuteAsync(context);
        snapshots.Add(new("management", "/management/v1/organizations", "post", 400, "application/problem+json", Body(context)));

        // Ignored test output, consumed by api:generate/api:check; no tracked files are written by tests.
        var output = Path.Combine(Root, "artifacts/contracts");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "dx002-wire.json"), JsonSerializer.Serialize(snapshots, Json));

        void Add(string path, string method, int status, object value) => snapshots.Add(
            new("management", path, method, status, "application/json", JsonSerializer.SerializeToElement(value, Json)));
    }

    [Fact]
    public async Task Stream_example_preserves_done_and_safe_error_envelopes()
    {
        var context = Context();
        var writer = new OpenAiCompletionWriter(context);
        await writer.WriteStreamEventAsync(new(ProviderStreamKind.TextDelta, Text: "Hello"),
            "example-chat", Guid.Parse("11111111-1111-4111-8111-111111111111"), CancellationToken.None);
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        await writer.FinishStreamAsync(CancellationToken.None);
        context.Response.Body.Position = 0;
        var success = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(Root, "docs/api/gateway-v1.openapi.json")));
        var sample = document.RootElement.GetProperty("paths").GetProperty("/v1/chat/completions")
            .GetProperty("post").GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("text/event-stream").GetProperty("example").GetString();
        var normalized = string.Join("\n\n", success.Split("\n\n").Select(frame =>
            frame.StartsWith("data: {", StringComparison.Ordinal)
                ? "data: " + JsonSerializer.Serialize(FixedTimestamp(JsonSerializer.Deserialize<JsonElement>(frame[6..]))) : frame));
        Assert.Equal(sample, normalized);

        context = Context();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        writer = new OpenAiCompletionWriter(context);
        await writer.WriteStreamErrorAsync("Synthetic interruption", "provider_error", CancellationToken.None);
        context.Response.Body.Position = 0;
        var stream = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("event: error\ndata: {\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
    }

    [Theory]
    [InlineData("\"temperature\":3")]
    [InlineData("\"max_completion_tokens\":0")]
    [InlineData("\"max_completion_tokens\":2147483648")]
    [InlineData("\"max_tokens\":1,\"max_completion_tokens\":1")]
    [InlineData("\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"lookup\"}}]")]
    [InlineData("\"response_format\":{}")]
    [InlineData("\"stop\":[\"\"]")]
    [InlineData("\"stream_options\":{\"unapproved\":true}")]
    public void Documented_request_rejections_are_enforced_by_real_parser(string fragment)
    {
        var body = "{\"model\":\"example-chat\",\"messages\":[{\"role\":\"user\",\"content\":\"Hello\"}]," + fragment + "}";
        Assert.Throws<GatewayRequestException>(() => ChatRequestParser.Parse(Encoding.UTF8.GetBytes(body)));
    }

    [Theory]
    [InlineData("gateway", "/v1/")]
    [InlineData("management", "/management/v1/")]
    public void Published_contracts_only_advertise_current_versioned_surfaces(string name, string prefix)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, $"docs/api/{name}-v1.openapi.json")));
        Assert.Equal("3.1.1", document.RootElement.GetProperty("openapi").GetString());
        Assert.Equal("1.0.0", document.RootElement.GetProperty("info").GetProperty("version").GetString());
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(item => item.Name).ToArray();
        Assert.All(paths, path => Assert.StartsWith(prefix, path));
        if (name == "gateway") Assert.Contains("/v1/responses", paths);
        if (name == "gateway") Assert.Contains("/v1/embeddings", paths);
        else Assert.DoesNotContain("/v1/embeddings", paths);
    }

    [Fact]
    public void Published_usage_export_contract_describes_binary_csv_bounds_auth_and_failures()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs/api/management-v1.openapi.json")));
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/management/v1/organizations/{organizationId}/usage/export").GetProperty("get");
        Assert.True(operation.GetProperty("security")[0].TryGetProperty("Session", out _));
        var responses = operation.GetProperty("responses");
        var success = responses.GetProperty("200");
        var content = success.GetProperty("content");
        Assert.Single(content.EnumerateObject());
        Assert.Equal("binary", content.GetProperty("text/csv").GetProperty("schema").GetProperty("format").GetString());
        Assert.Equal("no-store", success.GetProperty("headers").GetProperty("Cache-Control").GetProperty("schema").GetProperty("enum")[0].GetString());
        foreach (var header in new[] { "Content-Disposition", "X-Content-Type-Options", "X-Usage-From", "X-Usage-To", "X-Usage-Data-As-Of", "X-Usage-Source", "X-Usage-Pending-Buckets", "X-Usage-Row-Count" })
            Assert.True(success.GetProperty("headers").TryGetProperty(header, out _), header);
        foreach (var status in new[] { "400", "401", "403", "413", "504" })
            Assert.True(responses.TryGetProperty(status, out _), status);
        Assert.True(responses.GetProperty("413").GetProperty("content").TryGetProperty("application/problem+json", out _));
        var limit = operation.GetProperty("parameters").EnumerateArray().Single(value => value.GetProperty("name").GetString() == "rowLimit").GetProperty("schema");
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        Assert.Equal(5000, limit.GetProperty("maximum").GetInt32());
        Assert.Equal(1000, limit.GetProperty("default").GetInt32());
    }

    private DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonElement Body(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(context.Response.Body);
        return document.RootElement.Clone();
    }

    private static JsonElement FixedTimestamp(JsonElement value)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText())!;
        var field = node["object"]!.GetValue<string>() == "response" ? "created_at" : "created";
        Assert.True(node[field]!.GetValue<long>() > 0);
        node[field] = 1790812800;
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UZLLM.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root missing.");
    }

    private sealed record WireExample(string Document, string Path, string Method, int Status,
        string MediaType, JsonElement Body);

    public void Dispose() => services.Dispose();
}
