using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class PayloadRetentionIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Opt_in_encrypts_payload_and_opt_out_deletes_it_without_deleting_metadata()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var ids = await SeedAsync(db);
        var time = new FrozenClock(DateTimeOffset.UtcNow);
        var service = CreateService(db, time);
        var request = Encoding.UTF8.GetBytes("{\"secret\":\"private prompt\"}");
        var response = Encoding.UTF8.GetBytes("{\"answer\":\"private reply\"}");

        Assert.False((await service.GetPolicyAsync(ids.Org, ids.Project, default)).Enabled);
        Assert.False(await service.CaptureRequestAsync(ids.Org, ids.Project, ids.Request,
            request, default));
        Assert.Empty(await db.Set<UsagePayloadEntity>().ToArrayAsync());

        Assert.NotNull(await service.SetPolicyAsync(ids.Org, ids.Project, ids.Account, true, 60, default));
        Assert.True(await service.CaptureRequestAsync(ids.Org, ids.Project, ids.Request,
            request, default));
        await service.CaptureResponseAsync(ids.Org, ids.Project, ids.Request, response, default);
        var stored = await db.Set<UsagePayloadEntity>().AsNoTracking().SingleAsync();
        Assert.DoesNotContain("private prompt", Encoding.UTF8.GetString(stored.EncryptedRequestPayload));
        Assert.DoesNotContain("private reply", Encoding.UTF8.GetString(stored.EncryptedResponsePayload!));
        var retained = await service.GetPayloadAsync(ids.Org, ids.Project, ids.Request, default);
        Assert.Contains("private prompt", retained!.Request);
        Assert.Contains("private reply", retained.Response);
        Assert.Null(await service.GetPayloadAsync(Guid.NewGuid(), ids.Project, ids.Request, default));
        Assert.Null(await service.GetPayloadAsync(ids.Org, Guid.NewGuid(), ids.Request, default));

        await service.SetPolicyAsync(ids.Org, ids.Project, ids.Account, false, 60, default);
        Assert.Empty(await db.Set<UsagePayloadEntity>().AsNoTracking().ToArrayAsync());
        Assert.Null(await service.GetPayloadAsync(ids.Org, ids.Project, ids.Request, default));
        Assert.True(await db.Set<UsageRequestEntity>().AnyAsync(value => value.Id == ids.Request));
    }

    [Fact]
    public async Task Expiry_deletes_only_payload_and_shorter_policy_reduces_existing_expiry()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var ids = await SeedAsync(db);
        var time = new FrozenClock(DateTimeOffset.UtcNow);
        var service = CreateService(db, time);
        await service.SetPolicyAsync(ids.Org, ids.Project, ids.Account, true, 120, default);
        await service.CaptureRequestAsync(ids.Org, ids.Project, ids.Request,
            Encoding.UTF8.GetBytes("secret"), default);
        await service.SetPolicyAsync(ids.Org, ids.Project, ids.Account, true, 60, default);
        var expires = (await db.Set<UsagePayloadEntity>().AsNoTracking().SingleAsync()).ExpiresAt;
        Assert.InRange((expires - time.GetUtcNow().AddMinutes(60)).Duration(),
            TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        time.UtcNow = time.UtcNow.AddMinutes(61);
        Assert.Null(await service.GetPayloadAsync(ids.Org, ids.Project, ids.Request, default));
        Assert.Equal(1, await service.DeleteExpiredAsync(10, default));
        Assert.Equal(0, await service.DeleteExpiredAsync(10, default));
        Assert.True(await db.Set<UsageRequestEntity>().AnyAsync(value => value.Id == ids.Request));
    }

    private static PostgreSqlPayloadRetentionService CreateService(FoundationDbContext db, TimeProvider clock)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PayloadSecrets:ActiveKeyVersion"] = "v1",
            ["PayloadSecrets:Keys:v1"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())
        }).Build();
        return new(db, new PayloadEnvelopeProtector(config), clock);
    }

    private static async Task<(Guid Org, Guid Project, Guid Account, Guid Request)> SeedAsync(
        FoundationDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var account = Guid.CreateVersion7();
        var org = Guid.CreateVersion7();
        var project = Guid.CreateVersion7();
        var key = Guid.CreateVersion7();
        var model = Guid.CreateVersion7();
        var request = Guid.CreateVersion7();
        db.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
        {
            Id = account, Email = $"payload-{account:N}@example.uz", PasswordHash = "test",
            Status = "Active", CreatedAt = now, UpdatedAt = now
        });
        db.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = org, Name = "Payload tenant", Status = "Active", CreatedAt = now
        });
        db.Set<ProjectEntity>().Add(new ProjectEntity
        {
            Id = project, OrganizationId = org, Name = "Payload project", Status = "Active",
            SettingsJson = "{}", CreatedAt = now
        });
        db.Set<GatewayApiKeyEntity>().Add(new GatewayApiKeyEntity
        {
            Id = key, ProjectId = project, Name = "Payload key", Prefix = "123456789012",
            SecretFingerprint = RandomNumberGenerator.GetBytes(32), Status = "Active",
            CreatedByAccountId = account, CreatedAt = now
        });
        db.Set<CatalogModelEntity>().Add(new CatalogModelEntity
        {
            Id = model, CanonicalCode = "payload-test", DisplayName = "Payload test",
            ContextLength = 1000, MaxOutputTokens = 100, CapabilitiesJson = "[]",
            Status = "Active", CreatedAt = now
        });
        db.Set<UsageRequestEntity>().Add(new UsageRequestEntity
        {
            Id = request, OrganizationId = org, ProjectId = project, ApiKeyId = key,
            CanonicalModelId = model, StartedAt = now, ExecutionState = "Prepared",
            DeliveryState = "NotStarted", FinancialState = "PendingAdmission",
            Operation = "chat.completions"
        });
        await db.SaveChangesAsync();
        return (org, project, account, request);
    }

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
