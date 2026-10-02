using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class RetentionSweepIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Sweep_minimizes_only_expired_terminal_content_and_preserves_evidence_and_audit()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var ids = await SeedUsageAsync(db, now);
        var old = now.AddDays(-91);
        var processed = Guid.CreateVersion7();
        var atOperationalCutoff = Guid.CreateVersion7();
        var pending = Guid.CreateVersion7();
        var dead = Guid.CreateVersion7();
        var completedJob = Guid.CreateVersion7();
        var failedJob = Guid.CreateVersion7();
        var resolvedAlert = Guid.CreateVersion7();
        var unnotifiedAlert = Guid.CreateVersion7();
        var pendingAlert = Guid.CreateVersion7();
        await SeedOutboxAsync(db, processed, old, old);
        await SeedOutboxAsync(db, atOperationalCutoff, now.AddDays(-90), now.AddDays(-90));
        await SeedOutboxAsync(db, pending, old, null);
        await SeedOutboxAsync(db, dead, old, null, old);
        await SeedJobAsync(db, completedJob, old, old);
        await SeedJobAsync(db, failedJob, old, null, old);
        await SeedAlertAsync(db, resolvedAlert, old, old, old);
        await SeedAlertAsync(db, unnotifiedAlert, old, old, null);
        await SeedAlertAsync(db, pendingAlert, old, null, null);

        var sweep = new PostgreSqlRetentionSweep(db, ApprovedSchedule(), new FrozenClock(now));
        var result = await sweep.SweepAsync(10);

        Assert.Equal(new RetentionSweepResult(2, 1, 1, 1), result);
        var requests = await db.Set<UsageRequestEntity>().AsNoTracking().ToDictionaryAsync(value => value.Id);
        Assert.Equal(5, requests.Count);
        Assert.Null(requests[ids.OldSettled].TraceId);
        Assert.Null(requests[ids.OldReleased].TraceId);
        Assert.Equal("unresolved-trace", requests[ids.OldPending].TraceId);
        Assert.Equal("boundary-trace", requests[ids.AtCutoff].TraceId);
        Assert.Equal("recent-trace", requests[ids.RecentSettled].TraceId);
        Assert.Equal("Settled", requests[ids.OldSettled].FinancialState);
        Assert.Equal("{\"note\": \"immutable-evidence\"}", await db.Set<AuditEventEntity>().AsNoTracking()
            .Select(value => value.MetadataJson).SingleAsync());
        Assert.Equal("{}", await ScalarAsync("SELECT payload FROM ops.outbox WHERE id = @id", processed));
        Assert.Equal("{\"sensitive\": true}", await ScalarAsync("SELECT payload FROM ops.outbox WHERE id = @id", atOperationalCutoff));
        Assert.Equal("{\"sensitive\": true}", await ScalarAsync("SELECT payload FROM ops.outbox WHERE id = @id", pending));
        Assert.Equal("{\"sensitive\": true}", await ScalarAsync("SELECT payload FROM ops.outbox WHERE id = @id", dead));
        Assert.Equal("{}", await ScalarAsync("SELECT payload::text FROM ops.job WHERE id = @id", completedJob));
        Assert.Equal("{\"sensitive\": true}", await ScalarAsync("SELECT payload::text FROM ops.job WHERE id = @id", failedJob));
        Assert.Equal("{}", await ScalarAsync("SELECT details::text FROM ops.operational_alert WHERE id = @id", resolvedAlert));
        Assert.Equal("{\"sensitive\": true}", await ScalarAsync("SELECT details::text FROM ops.operational_alert WHERE id = @id", unnotifiedAlert));
        Assert.Equal("{\"sensitive\": true}", await ScalarAsync("SELECT details::text FROM ops.operational_alert WHERE id = @id", pendingAlert));
        Assert.Equal("dedup-" + completedJob.ToString("N"), await ScalarAsync(
            "SELECT deduplication_key FROM ops.job WHERE id = @id", completedJob));
        Assert.Equal(0, (await sweep.SweepAsync(10)).Total);
    }

    [Fact]
    public async Task Sweep_is_bounded_and_reapplies_to_old_content_after_restore()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await SeedOutboxAsync(db, first, now.AddDays(-91), now.AddDays(-91));
        await SeedOutboxAsync(db, second, now.AddDays(-91), now.AddDays(-91));
        var sweep = new PostgreSqlRetentionSweep(db, ApprovedSchedule(), new FrozenClock(now));

        Assert.Equal(0, (await new PostgreSqlRetentionSweep(db,
            new RetentionSchedule(null, 30, 90), new FrozenClock(now)).SweepAsync(1)).Total);
        Assert.Equal(1, (await sweep.SweepAsync(1)).OutboxMessages);
        Assert.Equal(1, (await sweep.SweepAsync(1)).OutboxMessages);
        Assert.Equal(0, (await sweep.SweepAsync(1)).OutboxMessages);
        // A PITR restore can reintroduce an old processed row. Re-run the same
        // sweep while traffic is quarantined before opening the restored DB.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ops.outbox SET payload = '{{\"sensitive\":true}}' WHERE id = {first}");
        Assert.Equal(1, (await sweep.SweepAsync(1)).OutboxMessages);
        Assert.Equal("{}", await ScalarAsync("SELECT payload FROM ops.outbox WHERE id = @id", first));
        Assert.Equal("{}", await ScalarAsync("SELECT payload FROM ops.outbox WHERE id = @id", second));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sweep.SweepAsync(0));
    }

    [Fact]
    public async Task Restore_scrub_disables_restored_opt_ins_and_deletes_all_payload_before_reopening()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var ids = await SeedUsageAsync(db, now);
        db.Set<UsagePayloadRetentionPolicyEntity>().Add(new UsagePayloadRetentionPolicyEntity
        {
            OrganizationId = ids.Org, ProjectId = ids.Project, Enabled = true,
            RetentionMinutes = 10080, UpdatedByAccountId = ids.Account, UpdatedAt = now.AddDays(-1)
        });
        db.Set<UsagePayloadEntity>().Add(new UsagePayloadEntity
        {
            RequestId = ids.RecentSettled, OrganizationId = ids.Org, ProjectId = ids.Project,
            EncryptedRequestPayload = [1, 2, 3], WrappedRequestKey = [4, 5, 6],
            RequestKeyVersion = "v1", CreatedAt = now.AddHours(-1), ExpiresAt = now.AddDays(1)
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RestorePayloadScrubber(db, new FrozenClock(now)).ScrubAsync(Guid.Empty));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RestorePayloadScrubber(db, new FrozenClock(now)).ScrubAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RestorePayloadScrubber(db, new FrozenClock(now)).ScrubAsync(ids.Account));
        Assert.True((await db.Set<UsagePayloadRetentionPolicyEntity>().AsNoTracking().SingleAsync()).Enabled);
        Assert.Single(await db.Set<UsagePayloadEntity>().AsNoTracking().ToListAsync());

        db.Set<IdentityOperatorAccessEntity>().Add(new IdentityOperatorAccessEntity
        {
            AccountId = ids.Account, IsActive = false
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RestorePayloadScrubber(db, new FrozenClock(now)).ScrubAsync(ids.Account));
        await db.Set<IdentityOperatorAccessEntity>()
            .Where(value => value.AccountId == ids.Account)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsActive, true));

        var result = await new RestorePayloadScrubber(db, new FrozenClock(now)).ScrubAsync(ids.Account);

        Assert.Equal(new RestorePayloadScrubResult(1, 1), result);
        Assert.False((await db.Set<UsagePayloadRetentionPolicyEntity>().AsNoTracking().SingleAsync()).Enabled);
        Assert.Empty(await db.Set<UsagePayloadEntity>().AsNoTracking().ToListAsync());
        Assert.True(await db.Set<UsageRequestEntity>().AsNoTracking().AnyAsync(value => value.Id == ids.RecentSettled));
        var audit = await db.Set<AuditEventEntity>().AsNoTracking()
            .SingleAsync(value => value.Action == "Privacy.RestorePayloadScrub");
        Assert.Equal(ids.Account, audit.ActorAccountId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson);
        Assert.Equal(1, metadata.RootElement.GetProperty("deletedPayloads").GetInt32());
        Assert.Equal(1, metadata.RootElement.GetProperty("disabledPolicies").GetInt32());
    }

    private async Task<string> ScalarAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.RuntimeConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static RetentionSchedule ApprovedSchedule() => new("policy-test", 30, 90);

    private static async Task<(Guid OldSettled, Guid OldReleased, Guid OldPending, Guid AtCutoff, Guid RecentSettled,
        Guid Account, Guid Org, Guid Project)> SeedUsageAsync(
        FoundationDbContext db, DateTimeOffset now)
    {
        var account = Guid.CreateVersion7();
        var org = Guid.CreateVersion7();
        var project = Guid.CreateVersion7();
        var key = Guid.CreateVersion7();
        var model = Guid.CreateVersion7();
        db.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
        {
            Id = account, Email = $"retention-{account:N}@example.uz", PasswordHash = "test",
            Status = "Active", CreatedAt = now, UpdatedAt = now
        });
        db.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = org, Name = "Retention tenant", Status = "Active", CreatedAt = now
        });
        db.Set<ProjectEntity>().Add(new ProjectEntity
        {
            Id = project, OrganizationId = org, Name = "Retention project", Status = "Active",
            SettingsJson = "{}", CreatedAt = now
        });
        db.Set<GatewayApiKeyEntity>().Add(new GatewayApiKeyEntity
        {
            Id = key, ProjectId = project, Name = "Retention key", Prefix = "123456789012",
            SecretFingerprint = RandomNumberGenerator.GetBytes(32), Status = "Active",
            CreatedByAccountId = account, CreatedAt = now
        });
        db.Set<CatalogModelEntity>().Add(new CatalogModelEntity
        {
            Id = model, CanonicalCode = "retention-test", DisplayName = "Retention test",
            ContextLength = 1000, MaxOutputTokens = 100, CapabilitiesJson = "[]",
            Status = "Active", CreatedAt = now
        });
        var oldSettled = Guid.CreateVersion7();
        var oldReleased = Guid.CreateVersion7();
        var oldPending = Guid.CreateVersion7();
        var atCutoff = Guid.CreateVersion7();
        var recentSettled = Guid.CreateVersion7();
        foreach (var (id, completed, financial, trace) in new[]
        {
            (oldSettled, now.AddDays(-31), "Settled", "expired-trace"),
            (oldReleased, now.AddDays(-31), "Released", "released-trace"),
            (oldPending, now.AddDays(-31), "PendingEvidence", "unresolved-trace"),
            (atCutoff, now.AddDays(-30), "Settled", "boundary-trace"),
            (recentSettled, now.AddDays(-1), "Settled", "recent-trace")
        })
        {
            db.Set<UsageRequestEntity>().Add(new UsageRequestEntity
            {
                Id = id, OrganizationId = org, ProjectId = project, ApiKeyId = key,
                CanonicalModelId = model, StartedAt = completed.AddMinutes(-1), CompletedAt = completed,
                ExecutionState = "Succeeded", DeliveryState = "Completed",
                FinancialState = financial, Operation = "chat.completions", TraceId = trace
            });
        }
        db.Set<AuditEventEntity>().Add(new AuditEventEntity
        {
            Id = Guid.CreateVersion7(), OrganizationId = org, ActorAccountId = account,
            Action = "RetentionTest", ResourceType = "Project", ResourceId = project,
            MetadataJson = "{\"note\":\"immutable-evidence\"}", OccurredAt = now.AddYears(-2)
        });
        await db.SaveChangesAsync();
        return (oldSettled, oldReleased, oldPending, atCutoff, recentSettled, account, org, project);
    }

    private static Task<int> SeedOutboxAsync(FoundationDbContext db, Guid id, DateTimeOffset occurred,
        DateTimeOffset? processed, DateTimeOffset? deadLettered = null) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ops.outbox (id, event_type, payload, occurred_at, available_at,
                processed_at, attempt_count, max_attempts, dead_lettered_at)
            VALUES ({id}, {"retention"}, CAST({"{\"sensitive\":true}"} AS jsonb), {occurred}, {occurred},
                {processed}, {1}, {3}, {deadLettered})
            """);

    private static Task<int> SeedJobAsync(FoundationDbContext db, Guid id, DateTimeOffset available,
        DateTimeOffset? completed, DateTimeOffset? deadLettered = null) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ops.job (id, job_type, payload, deduplication_key, available_at,
                completed_at, attempt_count, max_attempts, dead_lettered_at)
            VALUES ({id}, {"retention"}, CAST({"{\"sensitive\":true}"} AS jsonb), {"dedup-" + id.ToString("N")},
                {available}, {completed}, {1}, {3}, {deadLettered})
            """);

    private static Task<int> SeedAlertAsync(FoundationDbContext db, Guid id, DateTimeOffset occurred,
        DateTimeOffset? resolved, DateTimeOffset? notified) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ops.operational_alert (id, kind, severity, deduplication_key,
                details, occurred_at, resolved_at, notified_at)
            VALUES ({id}, {"RetentionTest"}, {"Warning"}, {"dedup-" + id.ToString("N")},
                CAST({"{\"sensitive\":true}"} AS jsonb), {occurred}, {resolved}, {notified})
            """);

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
