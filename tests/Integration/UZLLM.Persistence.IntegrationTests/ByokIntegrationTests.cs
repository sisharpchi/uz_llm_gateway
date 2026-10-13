using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Catalog.Contracts;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Modules.Providers.Contracts;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Persistence;
using UZLLM.Provider.Google;
using UZLLM.Provider.DeepSeek;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class ByokIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Theory]
    [InlineData("google", false)]
    [InlineData("google", true)]
    [InlineData("deepseek", false)]
    [InlineData("deepseek", true)]
    public async Task New_native_adapters_resolve_encrypted_tenant_keys_for_chat_and_sse_only(
        string providerCode, bool stream)
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "byok-native@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "Native BYOK");
        var project = await scope.ServiceProvider.GetRequiredService<IProjectService>()
            .CreateAsync(owner, org.Id, "Granted");
        var upstream = await scope.ServiceProvider.GetRequiredService<ICatalogService>()
            .AddProviderAsync(providerCode, "Native provider");
        var byok = scope.ServiceProvider.GetRequiredService<IByokCredentialService>();
        const string secret = "tenant-only-native-secret-1234";
        var key = await byok.CreateAsync(owner, org.Id, upstream.Id, "Native", secret);
        Assert.True(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, project.Id, true));
        var resolver = scope.ServiceProvider.GetRequiredService<IProviderCredentialResolver>();
        var handler = new NativeByokHandler(providerCode, stream);
        using var client = new HttpClient(handler) { BaseAddress = providerCode == "google"
            ? GoogleAdapterOptions.Production.BaseAddress : DeepSeekAdapterOptions.Production.BaseAddress };
        ILlmProviderAdapter adapter = providerCode == "google"
            ? new GoogleContentAdapter(client, resolver, GoogleAdapterOptions.Production)
            : new DeepSeekChatAdapter(client, resolver, DeepSeekAdapterOptions.Production);
        var execution = new ProviderExecutionContext(Guid.NewGuid(), upstream.Id, Guid.NewGuid(),
            key.Id, providerCode == "google" ? "gemini-test" : "deepseek-flash",
            TimeSpan.FromSeconds(5), org.Id, project.Id);
        var request = new ProviderChatRequest([new ProviderMessage("user",
            [new ProviderContentPart(ProviderContentKind.Text, "Hello")])], MaxOutputTokens: 100);
        ProviderUsage? usage;
        if (stream)
        {
            var events = new List<ProviderStreamEvent>();
            await foreach (var item in adapter.StreamAsync(request, execution)) events.Add(item);
            Assert.Equal("ok", string.Concat(events.Where(item => item.Kind == ProviderStreamKind.TextDelta).Select(item => item.Text)));
            Assert.Single(events, item => item.Kind == ProviderStreamKind.Finish);
            usage = Assert.Single(events, item => item.Kind == ProviderStreamKind.Usage).Usage;
            Assert.DoesNotContain(events, item => item.Kind == ProviderStreamKind.Error);
        }
        else
        {
            var response = await adapter.CompleteAsync(request, execution);
            Assert.Equal("ok", Assert.Single(response.Message.Content).Value);
            usage = response.Usage;
        }
        Assert.Equal(new ProviderUsage(4, 3, 1, 1), usage);
        Assert.Equal(providerCode == "google" ? secret : "Bearer " + secret, handler.Authentication);
        Assert.DoesNotContain(secret, handler.Uri!.AbsoluteUri);
        Assert.Equal(1, handler.Calls);
        async Task ExecuteAsync(ProviderExecutionContext context)
        {
            if (stream) { await foreach (var _ in adapter.StreamAsync(request, context)) { } }
            else await adapter.CompleteAsync(request, context);
        }
        var denied = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            ExecuteAsync(execution with { OrganizationId = Guid.NewGuid() }));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, denied.Error.Certainty);
        Assert.True(await byok.DisableAsync(owner, org.Id, key.Id));
        denied = await Assert.ThrowsAsync<ProviderExecutionException>(() => ExecuteAsync(execution));
        Assert.Equal(ProviderExecutionCertainty.NotDispatched, denied.Error.Certainty);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("google")]
    [InlineData("deepseek")]
    public async Task Restrictions_accept_only_provider_models_and_audited_lifetime_spend_cap(string providerCode)
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "byok-policy-owner@example.uz");
        var outsider = await RegisterAsync(scope, "byok-policy-outsider@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "BYOK policy");
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var upstream = await catalog.AddProviderAsync(providerCode, "Provider");
        var model = await catalog.AddModelAsync("policy-model", "Policy model", 1000, 100,
            [CatalogCapability.Text]);
        await catalog.AddProviderModelAsync(upstream.Id, model.Id, "upstream-model", null, null);
        var byok = scope.ServiceProvider.GetRequiredService<IByokCredentialService>();
        var key = await byok.CreateAsync(owner, org.Id, upstream.Id,
            "Restricted", "sk-restricted-example-1234");
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => byok.SetRestrictionsAsync(
            outsider, org.Id, key.Id, ["policy-model"], 50_000));
        await Assert.ThrowsAsync<ArgumentException>(() => byok.SetRestrictionsAsync(
            owner, org.Id, key.Id, ["unmapped-model"], 50_000));
        await Assert.ThrowsAsync<ArgumentException>(() => byok.SetRestrictionsAsync(
            owner, org.Id, key.Id, ["policy-model", "policy-model"], 50_000));
        await Assert.ThrowsAsync<ArgumentException>(() => byok.SetRestrictionsAsync(
            owner, org.Id, key.Id, ["policy-model"], -1));
        var restricted = await byok.SetRestrictionsAsync(owner, org.Id,
            key.Id, ["policy-model"], 50_000);
        Assert.Equal(["policy-model"], restricted!.AllowedModels);
        Assert.Equal(50_000, restricted.SpendLimitMicroUsd);
        Assert.Equal(0, restricted.ExternalSpentMicroUsd);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.Set<AuditEventEntity>().AsNoTracking().Where(value =>
            value.ResourceId == key.Id && value.Action == "byok.restrictions.updated").ToListAsync());
        Assert.True(await byok.DisableAsync(owner, org.Id, key.Id));
        Assert.Null(await byok.SetRestrictionsAsync(owner, org.Id, key.Id, null, null));
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("google")]
    [InlineData("deepseek")]
    public async Task Org_credential_lifecycle_is_masked_encrypted_audited_and_disabled(string providerCode)
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "byok-owner@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "BYOK tenant");
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var upstream = await catalog.AddProviderAsync(providerCode, "Provider");
        var byok = scope.ServiceProvider.GetRequiredService<IByokCredentialService>();
        var resolver = scope.ServiceProvider.GetRequiredService<IByokCredentialResolver>();
        const string firstSecret = "sk-first-secret-value-123456";
        await Assert.ThrowsAsync<ArgumentException>(() => byok.CreateAsync(owner,
            org.Id, upstream.Id, "Short", "too-short"));
        var unsupported = await catalog.AddProviderAsync("untrusted", "Untrusted");
        await Assert.ThrowsAsync<ArgumentException>(() => byok.CreateAsync(owner,
            org.Id, unsupported.Id, "Unknown", "sk-valid-length-123456"));

        var key = await byok.CreateAsync(owner, org.Id, upstream.Id, "Primary", firstSecret);
        Assert.Equal("••••3456", key.MaskedKey);
        Assert.DoesNotContain(firstSecret, JsonSerializer.Serialize(key), StringComparison.Ordinal);
        Assert.DoesNotContain(firstSecret, JsonSerializer.Serialize(await byok.ListAsync(owner, org.Id)),
            StringComparison.Ordinal);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var row = await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .SingleAsync(value => value.Id == key.Id);
        Assert.Equal(org.Id, row.OrganizationId);
        Assert.Equal("BYOK", row.CredentialType);
        Assert.DoesNotContain(firstSecret, Encoding.UTF8.GetString(row.EncryptedSecret));
        Assert.NotEqual(firstSecret, Encoding.UTF8.GetString(row.WrappedDataKey));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, Guid.NewGuid(), key.Id, upstream.Id));
        Assert.Equal(ByokTestStatus.Valid, await byok.TestAsync(owner, org.Id, key.Id));
        Assert.Equal("Valid", (await byok.FindAsync(owner, org.Id, key.Id))!.LastTestStatus);

        const string nextSecret = "sk-next-secret-value-987654";
        var rotated = await byok.UpdateAsync(owner, org.Id, key.Id, "Rotated", nextSecret);
        Assert.Equal("Rotated", rotated!.Name);
        Assert.Equal("••••7654", rotated.MaskedKey);
        Assert.Null(rotated.LastTestStatus);
        Assert.True(await byok.DisableAsync(owner, org.Id, key.Id));
        Assert.Null(await byok.TestAsync(owner, org.Id, key.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => byok.UpdateAsync(owner,
            org.Id, key.Id, "Cannot edit", null));
        Assert.True(await byok.DeleteAsync(owner, org.Id, key.Id));
        Assert.Null(await byok.FindAsync(owner, org.Id, key.Id));
        Assert.Empty(await byok.ListAsync(owner, org.Id));
        var deletedRow = await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .SingleAsync(value => value.Id == key.Id);
        Assert.Equal("deleted", deletedRow.KeyVersion);
        Assert.NotEqual(row.EncryptedSecret, deletedRow.EncryptedSecret);
        Assert.NotNull(deletedRow.DeletedAt);
        var auditRows = await db.Set<AuditEventEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == org.Id && value.ResourceId == key.Id)
            .ToArrayAsync();
        Assert.Equal(5, auditRows.Length);
        Assert.Contains(auditRows, value => value.Action == "byok.created");
        Assert.Contains(auditRows, value => value.Action == "byok.tested");
        Assert.Contains(auditRows, value => value.Action == "byok.updated");
        Assert.Contains(auditRows, value => value.Action == "byok.disabled");
        Assert.Contains(auditRows, value => value.Action == "byok.deleted");
        Assert.DoesNotContain(firstSecret, JsonSerializer.Serialize(auditRows));
        Assert.DoesNotContain(nextSecret, JsonSerializer.Serialize(auditRows));
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("google")]
    [InlineData("deepseek")]
    public async Task Credential_grants_require_same_tenant_active_project_and_prevent_cross_project_resolution(string providerCode)
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "byok-grants@example.uz");
        var outsider = await RegisterAsync(scope, "byok-outsider@example.uz");
        var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var org = await orgs.CreateAsync(owner, "BYOK grants");
        var otherOrg = await orgs.CreateAsync(owner, "Other grants");
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var allowed = await projects.CreateAsync(owner, org.Id, "Allowed");
        var denied = await projects.CreateAsync(owner, org.Id, "Denied");
        var foreign = await projects.CreateAsync(owner, otherOrg.Id, "Foreign");
        var upstream = await scope.ServiceProvider.GetRequiredService<ICatalogService>()
            .AddProviderAsync(providerCode, "Provider");
        var byok = scope.ServiceProvider.GetRequiredService<IByokCredentialService>();
        var resolver = scope.ServiceProvider.GetRequiredService<IByokCredentialResolver>();
        const string secret = "sk-antropic-test-value-12345";
        var key = await byok.CreateAsync(owner, org.Id, upstream.Id, "Anthropic", secret);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => byok.ListAsync(outsider, org.Id));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            byok.SetProjectGrantAsync(outsider, org.Id, key.Id, allowed.Id, true));
        db.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
        {
            OrganizationId = org.Id, AccountId = outsider, Role = "Developer",
            Status = "Active", CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => byok.ListAsync(outsider, org.Id));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            byok.SetProjectGrantAsync(outsider, org.Id, key.Id, allowed.Id, true));
        Assert.False(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, foreign.Id, true));
        Assert.True(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, true));
        Assert.True(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, true));
        Assert.Equal([allowed.Id], (await byok.FindAsync(owner, org.Id, key.Id))!.ProjectIds);
        Assert.Equal(secret, await resolver.ResolveGrantedSecretAsync(org.Id, allowed.Id,
            key.Id, upstream.Id));
        var executionResolver = scope.ServiceProvider.GetRequiredService<IProviderCredentialResolver>();
        var execution = new ProviderExecutionContext(Guid.NewGuid(), upstream.Id, Guid.NewGuid(),
            key.Id, "upstream-model", TimeSpan.FromSeconds(5), org.Id, allowed.Id);
        Assert.Equal(secret, await executionResolver.ResolveSecretAsync(execution));
        Assert.Null(await executionResolver.ResolveSecretAsync(execution with { ProjectId = denied.Id }));
        Assert.Null(await executionResolver.ResolveSecretAsync(execution with { OrganizationId = otherOrg.Id,
            ProjectId = foreign.Id }));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, denied.Id,
            key.Id, upstream.Id));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(otherOrg.Id, foreign.Id,
            key.Id, upstream.Id));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, allowed.Id,
            key.Id, Guid.NewGuid()));
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO gateway.provider_credential_project_grant (organization_id, credential_id, project_id, created_at) VALUES ({otherOrg.Id}, {key.Id}, {foreign.Id}, {DateTimeOffset.UtcNow})"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.True(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, false));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, allowed.Id,
            key.Id, upstream.Id));
        Assert.True(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, true));
        Assert.True(await projects.ArchiveAsync(owner, org.Id, allowed.Id));
        Assert.Null(await resolver.ResolveGrantedSecretAsync(org.Id, allowed.Id,
            key.Id, upstream.Id));
        Assert.False(await byok.SetProjectGrantAsync(owner, org.Id, key.Id, allowed.Id, true));
    }

    [Fact]
    public async Task Failed_audit_rolls_back_credential_creation_and_never_persists_secret()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var seedProvider = CreateProvider();
        await using var seed = seedProvider.CreateAsyncScope();
        var owner = await RegisterAsync(seed, "byok-rollback@example.uz");
        var org = await seed.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "BYOK rollback");
        var upstream = await seed.ServiceProvider.GetRequiredService<ICatalogService>()
            .AddProviderAsync("openai", "OpenAI");
        await using var failingProvider = CreateProvider(failAudit: true);
        await using var failingScope = failingProvider.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => failingScope.ServiceProvider
            .GetRequiredService<IByokCredentialService>().CreateAsync(owner, org.Id,
                upstream.Id, "Should rollback", "sk-rollback-secret-123456"));
        var db = seed.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Empty(await db.Set<ProviderCredentialEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == org.Id).ToArrayAsync());
    }

    private sealed class NativeByokHandler(string provider, bool stream) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Authentication { get; private set; }
        public Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Uri = request.RequestUri;
            Authentication = provider == "google" ? request.Headers.GetValues("x-goog-api-key").Single()
                : request.Headers.Authorization?.ToString();
            const string google = """
                {"responseId":"tenant-response","candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":4,"cachedContentTokenCount":1,"candidatesTokenCount":2,"thoughtsTokenCount":1,"totalTokenCount":7}}
                """;
            const string deepseek = """
                {"id":"tenant-response","object":"chat.completion","model":"deepseek-flash","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":4,"prompt_cache_hit_tokens":1,"prompt_cache_miss_tokens":3,"completion_tokens":3,"completion_tokens_details":{"reasoning_tokens":1},"total_tokens":7}}
                """;
            var body = provider == "google" ? google : deepseek;
            if (stream)
            {
                if (provider == "deepseek") body = body.Replace("\"message\":", "\"delta\":")
                    .Replace("\"chat.completion\"", "\"chat.completion.chunk\"");
                body = "data: " + body + "\n\n" + (provider == "deepseek" ? "data: [DONE]\n\n" : "");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, stream ? "text/event-stream" : "application/json") });
        }
    }

    private ServiceProvider CreateProvider(bool failAudit = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
            ["ProviderSecrets:ActiveKeyVersion"] = "v1",
            ["ProviderSecrets:Keys:v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        var services = new ServiceCollection().AddUzllmPersistence(configuration).AddUzllmIdentity()
            .AddUzllmAudit().AddUzllmOrganizations().AddUzllmProjects()
            .AddUzllmCatalog().AddUzllmProviders(configuration)
            .AddScoped<IByokCredentialVerifier, NoTransactionVerifier>();
        if (failAudit) services.AddScoped<IAuditEventStore, FailingAuditStore>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<Guid> RegisterAsync(AsyncServiceScope scope, string email)
    {
        var registration = await scope.ServiceProvider.GetRequiredService<IIdentityService>()
            .RegisterAsync(email, "correct horse battery staple");
        Assert.True(await scope.ServiceProvider.GetRequiredService<IIdentityService>()
            .VerifyEmailAsync(registration.VerificationToken));
        return registration.AccountId;
    }

    private sealed class NoTransactionVerifier(FoundationDbContext db) : IByokCredentialVerifier
    {
        public Task<ByokTestStatus> VerifyAsync(string providerCode, string secret,
            CancellationToken cancellationToken = default)
        {
            Assert.Null(db.Database.CurrentTransaction);
            return Task.FromResult(ByokTestStatus.Valid);
        }
    }

    private sealed class FailingAuditStore : IAuditEventStore
    {
        public Task AppendAsync(AuditEvent auditEvent,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated audit storage failure.");
    }
}
