using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Billing.Infrastructure;

namespace UZLLM.Billing.Tests;

public sealed class ReportingEvidencePolicyTests
{
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static ReportingEvidenceProfile Profile() => new("synthetic", 1, true, "issuer-test", "jurisdiction-test", "series-test",
        "TEST", 3, "Invoice", "issuer-snapshot-test", "tax-policy-test", "correction-policy-test", "archive-policy-test", From, From.AddYears(2),
        [.. new[] { "Finance", "Legal", "Security", "Operations", "Commercial" }.Select(role => new ReportingApproval(role, "test-reviewer", "test-approval", From.AddDays(-1)))],
        [new("chat-test", "test", "Chat", "Sse")]);
    private static RegisterFiscalEvidenceRequest Fiscal() => new("synthetic", 1, Guid.NewGuid(), "external-operation", "Registered",
        "external-process-test", "Synthetic external evidence", new("TEST-001", From, "Invoice", "customer-snapshot-test",
            new("9007199254740993", null, null), new('a', 64), "controlled-archive-test", "issuer-verification-test"));
    private static RegisterContractReferenceRequest Contract() => new("synthetic", 1, Guid.NewGuid(), "contract-test", "policy-v1", "chat-test",
        "customer-snapshot-test", From, From.AddYears(1), "EvidenceOnly", "Synthetic contract evidence");

    [Fact]
    public void Default_registry_and_live_flag_never_substitute_for_approved_profile()
    {
        Assert.Throws<ReportingProfileUnavailableException>(() => ReportingEvidencePolicy.Resolve(new(false, []), "missing", 1, From));
        var live = Profile() with { Synthetic = false };
        Assert.Throws<ReportingProfileUnavailableException>(() => ReportingEvidencePolicy.Resolve(new(false, [live]), live.Id, 1, From));
        Assert.False(ReportingEvidencePolicy.Resolve(new(true, [live]), live.Id, 1, From).Synthetic);
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Resolve(new(true, [live with { Approvals = [] }]), live.Id, 1, From));
    }

    [Theory]
    [InlineData(-1, false)] [InlineData(0, true)] [InlineData(1, true)] [InlineData(729, true)] [InlineData(730, false)]
    public void Profile_window_is_from_inclusive_to_exclusive(int days, bool accepted)
    {
        var p = Profile(); var registry = new ReportingEvidenceRegistry(false, [p]);
        if (accepted) Assert.True(ReportingEvidencePolicy.Resolve(registry, p.Id, 1, From.AddDays(days)).Synthetic);
        else Assert.Throws<ReportingProfileUnavailableException>(() => ReportingEvidencePolicy.Resolve(registry, p.Id, 1, From.AddDays(days)));
    }
    [Fact]
    public void Revocation_fence_and_frozen_approval_arrays_preserve_historical_profile()
    {
        var p = Profile() with { RevokedAt = From.AddDays(1) }; var registry = new ReportingEvidenceRegistry(false, [p]);
        var frozen = ReportingEvidencePolicy.Resolve(registry, p.Id, 1, From);
        var digest = ReportingEvidencePolicy.Digest(frozen); p.Approvals[0] = p.Approvals[0] with { Reference = "changed" };
        Assert.Equal(digest, ReportingEvidencePolicy.Digest(frozen));
        Assert.Throws<ReportingProfileUnavailableException>(() => ReportingEvidencePolicy.Resolve(registry, p.Id, 1, From.AddDays(1)));
    }
    [Theory]
    [InlineData(-1, false)] [InlineData(0, true)] [InlineData(6, true)] [InlineData(7, false)]
    public void Profile_scale_is_explicit_and_bounded_not_assumed_two_decimals(int scale, bool valid)
    {
        var p = Profile() with { UnitScale = scale };
        if (valid) ReportingEvidencePolicy.ValidateProfile(p);
        else Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.ValidateProfile(p));
    }
    [Fact]
    public void Duplicate_missing_or_late_approval_and_unregistered_scope_shapes_fail_closed()
    {
        var p = Profile();
        foreach (var invalid in new[] { p with { Approvals = p.Approvals.Skip(1).ToArray() },
            p with { Approvals = [..p.Approvals, p.Approvals[0]] },
            p with { Approvals = [p.Approvals[0] with { ApprovedAt = From.AddSeconds(1) }, ..p.Approvals.Skip(1)] },
            p with { Scopes = [p.Scopes[0], p.Scopes[0]] }, p with { Scopes = [p.Scopes[0] with { Id = "*" }] },
            p with { Scopes = [p.Scopes[0] with { Operation = "caller-sql" }] } })
            Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.ValidateProfile(invalid));
    }
    [Theory]
    [InlineData("http://127.0.0.1/a")] [InlineData("https://archive.example/a")] [InlineData("../file")]
    [InlineData("file.exe/path")] [InlineData("mailto:test@example.test")] [InlineData("reference\n")] [InlineData("")]
    public void Controlled_references_reject_fetch_urls_paths_and_control_characters(string value) =>
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Reference(value));

    [Fact]
    public void Exact_profile_units_preserve_unknown_tax_and_large_values_without_USD_or_FX_conversion()
    {
        var value = ReportingEvidencePolicy.Fiscal(Fiscal());
        Assert.Equal("9007199254740993", value.Document!.Amounts.TotalUnits); Assert.Null(value.Document.Amounts.TaxUnits);
        Assert.Null(value.Document.Amounts.NetUnits);
        var original = Fiscal(); var known = original with { Document = original.Document! with { Amounts = new("1001", "1000", "1") } };
        Assert.Equal("1", ReportingEvidencePolicy.Fiscal(known).Document!.Amounts.TaxUnits);
        var zero = original with { Document = original.Document! with { Amounts = new("1", "1", "0") } };
        Assert.Equal("0", ReportingEvidencePolicy.Fiscal(zero).Document!.Amounts.TaxUnits);
    }
    [Theory]
    [InlineData("0", null, null)] [InlineData("-1", null, null)] [InlineData("01", null, null)] [InlineData("1.0", null, null)]
    [InlineData("1e3", null, null)] [InlineData("100000000000000000000000000000000000000", null, null)]
    [InlineData("10", "9", null)] [InlineData("10", null, "1")] [InlineData("10", "8", "1")]
    public void Monetary_bounds_and_explicit_breakdown_reject_nonexact_or_inconsistent_units(string total, string? net, string? tax)
    {
        var r = Fiscal(); Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Fiscal(r with
        { Document = r.Document! with { Amounts = new(total, net, tax) } }));
    }
    [Fact]
    public void Exact_38_digit_boundary_and_zero_net_are_valid()
    {
        var r = Fiscal(); var max = new string('9', 38);
        Assert.Equal(max, ReportingEvidencePolicy.Fiscal(r with { Document = r.Document! with { Amounts = new(max, "0", max) } }).Document!.Amounts.TotalUnits);
    }
    [Theory]
    [InlineData("Unknown")] [InlineData("Cancelled")]
    public void Unknown_or_cancelled_observation_cannot_publish_document_or_statement(string state)
    {
        var r = Fiscal() with { Outcome = state };
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Fiscal(r));
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Fiscal(r with { Document = null, StatementVersionId = Guid.NewGuid() }));
        Assert.Null(ReportingEvidencePolicy.Fiscal(r with { Document = null }).Document);
    }
    [Fact]
    public void Contract_and_link_remain_evidence_only_and_reject_unbounded_or_unsigned_claim_inputs()
    {
        var r = Contract(); Assert.Equal("EvidenceOnly", ReportingEvidencePolicy.Contract(r).State);
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Contract(r with { State = "EligibleForCredit" }));
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Contract(r with { SignedContractReference = "signed-ref" }));
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Contract(r with { EffectiveTo = r.EffectiveFrom }));
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Link(new(Guid.NewGuid(), Guid.NewGuid(), From, "No report selected")));
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Link(new(Guid.NewGuid(), Guid.NewGuid(), From.AddDays(1), "Wrong period selected", Guid.NewGuid())));
        var link = ReportingEvidencePolicy.Link(new(Guid.NewGuid(), Guid.NewGuid(), From, "Selected immutable report", Guid.NewGuid()));
        Assert.Equal(From, link.PeriodStart);
    }
    [Theory]
    [InlineData("{\"profileId\":\"synthetic\",\"taxRate\":0.12}")]
    [InlineData("{\"profileId\":\"synthetic\",\"archiveUrl\":\"https://example.test\"}")]
    public void Fiscal_wire_rejects_undeclared_issuance_and_tax_overrides(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RegisterFiscalEvidenceRequest>(json, ReportingEvidencePolicy.Json));

    [Fact]
    public void Missing_registered_document_invalid_digest_and_control_reason_are_rejected()
    {
        var r = Fiscal();
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Fiscal(r with { Document = null }));
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Fiscal(r with { Document = r.Document! with { ContentSha256 = new string('A', 64) } }));
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Fiscal(r with { Reason = "external\nsecret" }));
        Assert.Equal(r.Reason, ReportingEvidencePolicy.Fiscal(r with { Reason = " " + r.Reason + " " }).Reason);
    }
    [Theory]
    [InlineData("Testing", true)] [InlineData("Production", false)]
    public void Deployment_binding_rejects_synthetic_profiles_in_Production(string environment, bool accepted)
    {
        var json = JsonSerializer.Serialize(new { Reporting = new { Evidence = new ReportingEvidenceRegistry(false, [Profile()]) } });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var config = new ConfigurationBuilder().AddJsonStream(stream).Build();
        Assert.True(config.GetSection("Reporting:Evidence").Exists(), json);
        Assert.Equal("synthetic", config["Reporting:Evidence:Profiles:0:Id"]);
        var services = new ServiceCollection(); var env = new EvidenceEnvironment(environment);
        if (accepted)
        {
            services.AddUzllmReportingEvidence(config, env); using var provider = services.BuildServiceProvider();
            Assert.True(Assert.Single(provider.GetRequiredService<ReportingEvidenceRegistry>().Profiles).Synthetic);
            Assert.False(provider.GetRequiredService<ReportingEvidenceRegistry>().AllowLiveRegistration);
        }
        else Assert.Throws<ArgumentException>(() => services.AddUzllmReportingEvidence(config, env));
    }
    private sealed class EvidenceEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "EvidenceTests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Partial_deployment_configuration_preserves_disabled_and_empty_defaults(bool withProfiles)
    {
        var evidence = new Dictionary<string, object>();
        if (withProfiles) evidence["Profiles"] = new[] { Profile() };
        else evidence["AllowLiveRegistration"] = false;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Reporting = new { Evidence = evidence } })));
        var config = new ConfigurationBuilder().AddJsonStream(stream).Build(); var services = new ServiceCollection();
        services.AddUzllmReportingEvidence(config, new EvidenceEnvironment("Testing")); using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ReportingEvidenceRegistry>(); Assert.False(registry.AllowLiveRegistration);
        Assert.Equal(withProfiles ? 1 : 0, registry.Profiles.Length);
    }
    [Theory]
    [InlineData("IssuerId")] [InlineData("DocumentKind")]
    public void Invalid_deployment_profile_is_rejected_instead_of_silently_dropped_from_the_array(string missing)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new { Reporting = new { Evidence = new ReportingEvidenceRegistry(false, [Profile()]) } }))!;
        json["Reporting"]!["Evidence"]!["Profiles"]![0]!.AsObject().Remove(missing);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var config = new ConfigurationBuilder().AddJsonStream(stream).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddUzllmReportingEvidence(config, new EvidenceEnvironment("Testing")));
    }
    [Theory]
    [InlineData("chat-test", "test", "Chat", "Sse", true)]
    [InlineData("different", "test", "Chat", "Sse", false)]
    [InlineData("chat-test", "other", "Chat", "Sse", false)]
    [InlineData("chat-test", "test", "Responses", "Sse", false)]
    [InlineData("chat-test", "test", "Chat", "Json", false)]
    public void Contract_report_scope_requires_each_independent_identity_region_operation_and_mode(string id, string region, string operation, string mode, bool accepted)
    {
        var expected = new ReportingServiceScope("chat-test", "test", "Chat", "Sse"); var actual = new ReportingServiceScope(id, region, operation, mode);
        if (accepted) ReportingEvidencePolicy.EnsureScope(expected, actual);
        else Assert.Throws<ReportingEvidenceConflictException>(() => ReportingEvidencePolicy.EnsureScope(expected, actual));
    }
    [Fact]
    public void UTC_input_rejects_offset_and_submicrosecond_loss_instead_of_changing_signed_terms()
    {
        ReportingEvidencePolicy.Utc(From.AddTicks(10));
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Utc(From.AddTicks(1)));
        Assert.Throws<ArgumentException>(() => ReportingEvidencePolicy.Utc(From.ToOffset(TimeSpan.FromHours(5))));
    }
}
