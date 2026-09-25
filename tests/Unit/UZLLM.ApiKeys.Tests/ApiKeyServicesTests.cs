using System.Security.Cryptography;
using UZLLM.Modules.ApiKeys.Application;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.ApiKeys.Infrastructure;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Persistence;

namespace UZLLM.ApiKeys.Tests;

public sealed class ApiKeyServicesTests
{
    [Fact]
    public void Gateway_key_generator_creates_parseable_high_entropy_keys_and_rejects_malformed_values()
    {
        var generator = new GatewayApiKeySecretGenerator();

        var secret = generator.Create();

        Assert.StartsWith("uzllm_live_", secret.Value, StringComparison.Ordinal);
        Assert.Equal(12, secret.Prefix.Length);
        Assert.True(generator.TryParse(secret.Value, out var parsed));
        Assert.Equal(secret, parsed);
        Assert.False(generator.TryParse("uzllm_live_NOTHEX_short", out _));
    }

    [Fact]
    public void Hmac_fingerprint_matches_only_the_original_key_without_storing_plaintext()
    {
        var fingerprint = new HmacApiKeySecretFingerprint(RandomNumberGenerator.GetBytes(32));

        var storedFingerprint = fingerprint.Create("uzllm_live_123456abcdef_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.True(fingerprint.Matches("uzllm_live_123456abcdef_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", storedFingerprint));
        Assert.False(fingerprint.Matches("uzllm_live_123456abcdef_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", storedFingerprint));
        Assert.NotEqual("uzllm_live_123456abcdef_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Convert.ToBase64String(storedFingerprint));
    }

    [Fact]
    public async Task CreateAsync_returns_the_full_secret_once_but_persists_only_a_fingerprint_and_audits_the_event()
    {
        var fixture = new ApiKeyFixture();

        var issued = await fixture.Service.CreateAsync(fixture.OwnerAccountId, fixture.Project.Id, " Production ", null);
        var listed = await fixture.Service.ListAsync(fixture.OwnerAccountId, fixture.Project.Id);

        Assert.Equal("uzllm_live_123456abcdef_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", issued.Secret);
        var listedKey = Assert.Single(listed);
        Assert.Equal("Production", listedKey.Name);
        Assert.Equal(issued.ApiKey.Id, listedKey.Id);
        var stored = Assert.Single(fixture.Store.Stored.Values);
        Assert.NotEqual(issued.Secret, Convert.ToBase64String(stored.SecretFingerprint));
        var audit = Assert.Single(fixture.Audit.Events);
        Assert.Equal("api_key.created", audit.Action);
        Assert.DoesNotContain(issued.Secret, audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticateAsync_accepts_only_active_unexpired_keys_for_active_projects()
    {
        var fixture = new ApiKeyFixture();
        var issued = await fixture.Service.CreateAsync(fixture.OwnerAccountId, fixture.Project.Id, "Production", null);

        var authenticated = await fixture.Authenticator.AuthenticateAsync(issued.Secret);

        Assert.NotNull(authenticated);
        Assert.Equal(issued.ApiKey.Id, authenticated.ApiKeyId);
        Assert.Equal(fixture.Project.Id, authenticated.ProjectId);
        Assert.Null(await fixture.Authenticator.AuthenticateAsync(issued.Secret + "0"));

        await fixture.Service.SetStatusAsync(fixture.OwnerAccountId, issued.ApiKey.Id, GatewayApiKeyStatus.Disabled);
        Assert.Null(await fixture.Authenticator.AuthenticateAsync(issued.Secret));
    }

    [Fact]
    public async Task AuthenticateAsync_rejects_expired_and_archived_project_keys()
    {
        var fixture = new ApiKeyFixture();
        var expired = await fixture.Service.CreateAsync(fixture.OwnerAccountId, fixture.Project.Id, "Expired", fixture.Clock.GetUtcNow().AddMinutes(1));

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await fixture.Authenticator.AuthenticateAsync(expired.Secret));

        fixture.Store.ProjectActive = false;
        Assert.Null(await fixture.Authenticator.AuthenticateAsync(expired.Secret));
    }

    [Fact]
    public async Task Create_list_and_status_change_reject_a_non_owner()
    {
        var fixture = new ApiKeyFixture();
        var outsider = Guid.CreateVersion7();

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => fixture.Service.CreateAsync(outsider, fixture.Project.Id, "Production", null));
        var issued = await fixture.Service.CreateAsync(fixture.OwnerAccountId, fixture.Project.Id, "Production", null);
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => fixture.Service.ListAsync(outsider, fixture.Project.Id));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => fixture.Service.SetStatusAsync(outsider, issued.ApiKey.Id, GatewayApiKeyStatus.Disabled));
    }

    [Fact]
    public async Task CreateAsync_rejects_expiration_that_is_not_in_the_future()
    {
        var fixture = new ApiKeyFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(fixture.OwnerAccountId, fixture.Project.Id, "Production", fixture.Clock.GetUtcNow()));
    }

    [Fact]
    public async Task RotateAsync_preserves_identity_and_revokes_old_secret_without_auditing_plaintext()
    {
        var fixture = new ApiKeyFixture();
        var original = await fixture.Service.CreateAsync(fixture.OwnerAccountId, fixture.Project.Id,
            "Production", null);

        var rotated = await fixture.Service.RotateAsync(fixture.OwnerAccountId, original.ApiKey.Id);

        Assert.NotNull(rotated);
        Assert.Equal(original.ApiKey.Id, rotated.ApiKey.Id);
        Assert.Equal(original.ApiKey.ProjectId, rotated.ApiKey.ProjectId);
        Assert.Equal(2, rotated.ApiKey.Generation);
        Assert.NotEqual(original.Secret, rotated.Secret);
        Assert.Null(await fixture.Authenticator.AuthenticateAsync(original.Secret));
        Assert.Equal(original.ApiKey.Id,
            (await fixture.Authenticator.AuthenticateAsync(rotated.Secret))!.ApiKeyId);
        Assert.Equal(rotated.ApiKey.Prefix,
            (await fixture.Service.ListAsync(fixture.OwnerAccountId, fixture.Project.Id)).Single().Prefix);
        var audit = Assert.Single(fixture.Audit.Events, value => value.Action == "api_key.rotated");
        Assert.DoesNotContain(rotated.Secret, audit.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain(original.Secret, audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RotateAsync_rejects_outsider_disabled_and_expired_keys()
    {
        var fixture = new ApiKeyFixture();
        var issued = await fixture.Service.CreateAsync(fixture.OwnerAccountId, fixture.Project.Id,
            "Production", null);
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            fixture.Service.RotateAsync(Guid.CreateVersion7(), issued.ApiKey.Id));
        Assert.True(await fixture.Service.SetStatusAsync(fixture.OwnerAccountId, issued.ApiKey.Id,
            GatewayApiKeyStatus.Disabled));
        Assert.Null(await fixture.Service.RotateAsync(fixture.OwnerAccountId, issued.ApiKey.Id));
        Assert.Equal(1, fixture.Store.Stored[issued.ApiKey.Id].ApiKey.Generation);

        var expiring = await fixture.Service.CreateAsync(fixture.OwnerAccountId, fixture.Project.Id,
            "Expiring", fixture.Clock.GetUtcNow().AddMinutes(1));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await fixture.Service.RotateAsync(fixture.OwnerAccountId, expiring.ApiKey.Id));
    }
}

internal sealed class ApiKeyFixture
{
    public ApiKeyFixture()
    {
        OwnerAccountId = Guid.CreateVersion7();
        Project = new Project(Guid.CreateVersion7(), Guid.CreateVersion7(), "Production", ProjectStatus.Active, "{}", Clock.GetUtcNow(), null);
        ProjectAccess = new InMemoryProjectAccessService(Project, OwnerAccountId);
        Service = new ApiKeyService(Store, ProjectAccess, new DeterministicApiKeySecretGenerator(), Fingerprint, Audit, new NoopTransactionCoordinator(), Clock);
        Authenticator = new ApiKeyAuthenticator(Store, new DeterministicApiKeySecretGenerator(), Fingerprint, Clock);
    }

    public Guid OwnerAccountId { get; }
    public Project Project { get; }
    public AdjustableApiKeyTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 24, 16, 0, 0, TimeSpan.Zero));
    public InMemoryApiKeyStore Store { get; } = new();
    public InMemoryProjectAccessService ProjectAccess { get; }
    public HmacApiKeySecretFingerprint Fingerprint { get; } = new(RandomNumberGenerator.GetBytes(32));
    public RecordingApiKeyAuditTrail Audit { get; } = new();
    public IApiKeyService Service { get; }
    public IApiKeyAuthenticator Authenticator { get; }
}

internal sealed class AdjustableApiKeyTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now;
    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan duration) => current = current.Add(duration);
}

internal sealed class DeterministicApiKeySecretGenerator : IApiKeySecretGenerator
{
    private const string Value = "uzllm_live_123456abcdef_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private int generation;
    public ApiKeySecret Create()
    {
        var number = Interlocked.Increment(ref generation);
        if (number == 1) return new(Value, "123456abcdef");
        var prefix = number.ToString("x12");
        return new ApiKeySecret($"uzllm_live_{prefix}_{new string('b', 64)}", prefix);
    }
    public bool TryParse(string value, out ApiKeySecret secret)
    {
        secret = default!;
        if (string.Equals(value, Value, StringComparison.Ordinal))
        {
            secret = new ApiKeySecret(Value, "123456abcdef");
            return true;
        }
        if (!new GatewayApiKeySecretGenerator().TryParse(value, out secret)) return false;
        return true;
    }
}

internal sealed class InMemoryApiKeyStore : IApiKeyStore
{
    public Dictionary<Guid, MutableStoredApiKey> Stored { get; } = [];
    public bool ProjectActive { get; set; } = true;
    public HashSet<string> HistoricalPrefixes { get; } = [];

    public Task<bool> TryCreateAsync(StoredGatewayApiKey apiKey, CancellationToken cancellationToken = default)
    {
        if (Stored.Values.Any(existing => existing.ApiKey.Prefix == apiKey.ApiKey.Prefix)) return Task.FromResult(false);
        Stored.Add(apiKey.ApiKey.Id, new MutableStoredApiKey(apiKey.ApiKey, apiKey.SecretFingerprint));
        HistoricalPrefixes.Add(apiKey.ApiKey.Prefix);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<GatewayApiKey>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GatewayApiKey>>(Stored.Values.Where(value => value.ApiKey.ProjectId == projectId).Select(value => value.ApiKey).ToList());

    public Task<GatewayApiKey?> FindByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Stored.GetValueOrDefault(apiKeyId)?.ApiKey);

    public Task<StoredGatewayApiKey?> FindAuthenticationCandidateAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(Stored.Values.SingleOrDefault(value => value.ApiKey.Prefix == prefix) is { } candidate
            ? new StoredGatewayApiKey(candidate.ApiKey, candidate.SecretFingerprint, ProjectActive)
            : null);

    public Task<bool> TrySetStatusAsync(Guid apiKeyId, GatewayApiKeyStatus status, CancellationToken cancellationToken = default)
    {
        if (Stored.GetValueOrDefault(apiKeyId) is not { } stored || stored.ApiKey.Status == status) return Task.FromResult(false);
        stored.ApiKey = stored.ApiKey with { Status = status };
        return Task.FromResult(true);
    }

    public Task<ApiKeyRotationStoreResult> TryRotateAsync(Guid apiKeyId, int expectedGeneration,
        string newPrefix, byte[] newFingerprint, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (HistoricalPrefixes.Contains(newPrefix)) return Task.FromResult(ApiKeyRotationStoreResult.PrefixCollision);
        if (Stored.GetValueOrDefault(apiKeyId) is not { } stored || stored.ApiKey.Generation != expectedGeneration
            || stored.ApiKey.Status != GatewayApiKeyStatus.Active || stored.ApiKey.ExpiresAt <= now || !ProjectActive)
            return Task.FromResult(ApiKeyRotationStoreResult.Conflict);
        stored.ApiKey = stored.ApiKey with { Prefix = newPrefix, Generation = expectedGeneration + 1 };
        stored.SecretFingerprint = newFingerprint;
        HistoricalPrefixes.Add(newPrefix);
        return Task.FromResult(ApiKeyRotationStoreResult.Rotated);
    }
}

internal sealed class MutableStoredApiKey(GatewayApiKey apiKey, byte[] secretFingerprint)
{
    public GatewayApiKey ApiKey { get; set; } = apiKey;
    public byte[] SecretFingerprint { get; set; } = secretFingerprint;
}

internal sealed class InMemoryProjectAccessService(Project project, Guid ownerAccountId) : IProjectAccessService
{
    public Task<Project?> GetOwnedAsync(Guid accountId, Guid projectId, CancellationToken cancellationToken = default)
    {
        if (projectId != project.Id) return Task.FromResult<Project?>(null);
        if (accountId != ownerAccountId) throw new TenantAccessDeniedException();
        return Task.FromResult<Project?>(project);
    }
}

internal sealed class RecordingApiKeyAuditTrail : IAuditTrail
{
    public List<AuditEvent> Events { get; } = [];
    public Task<AuditEvent> RecordAsync(AuditEventInput input, CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent(Guid.CreateVersion7(), input.OrganizationId, input.ActorAccountId, input.Action, input.ResourceType, input.ResourceId, input.IpAddress, input.MetadataJson ?? "{}", DateTimeOffset.UtcNow);
        Events.Add(auditEvent);
        return Task.FromResult(auditEvent);
    }
}

internal sealed class NoopTransactionCoordinator : ITransactionCoordinator
{
    public Task<ITransactionScope> BeginAsync(CancellationToken cancellationToken = default) => Task.FromResult<ITransactionScope>(new NoopTransactionScope());
}

internal sealed class NoopTransactionScope : ITransactionScope
{
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
