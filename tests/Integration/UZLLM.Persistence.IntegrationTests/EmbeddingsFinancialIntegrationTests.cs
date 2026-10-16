using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Persistence.IntegrationTests;

public sealed partial class BillingFinancialIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Embeddings_input_only_evidence_survives_scope_loss_and_settles_once(bool unknown)
    {
        await ResetAsync();
        var seed = await SeedAsync(embedding: true);
        await CreditAsync(seed.OrganizationId, 100_000);
        var key = Guid.NewGuid().ToString();
        Reservation hold;
        Guid attemptId;
        await using (var provider = fixture.CreateServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.False(db.Database.HasPendingModelChanges());
            var migrationId = Assert.Single(db.GetService<IMigrationsAssembly>().Migrations.Keys,
                id => id.EndsWith("_AddEmbeddingModelLimits", StringComparison.Ordinal));
            Assert.True(db.GetService<IMigrationsIdGenerator>().IsValidId(migrationId));
            Assert.Equal("AddEmbeddingModelLimits", db.GetService<IMigrationsIdGenerator>().GetName(migrationId));
            await scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>().MigrateAsync(migrationId);
            var finance = Financial(scope.ServiceProvider, new MutableFinancialClock(Start));
            var admitted = await finance.ReserveAsync(Admission(seed, 30_000, key, operation: "embeddings"));
            hold = admitted.Reservation!;
            Assert.NotNull(hold);
            var usage = Usage(scope.ServiceProvider, new MutableFinancialClock(Start));
            var attempt = await usage.StartAttemptAsync(hold.RequestId, seed.ProviderModelId);
            attemptId = attempt.Id;
            Assert.True(await usage.MarkDispatchedAsync(attemptId));
            if (unknown) await usage.RecordUnknownAsync(hold.RequestId, attemptId);
            else await usage.RecordVerifiedAsync(new(hold.RequestId, attemptId, EvidenceSource.Provider, 12, 0, 0, null, seed.PriceId, "embed-native"));
            // Leave settlement to a different scope/worker, as after a process crash.
        }
        await using var recovery = fixture.CreateServiceProvider();
        await using var recoveredScope = recovery.CreateAsyncScope();
        var services = recoveredScope.ServiceProvider;
        var financial = Financial(services, new MutableFinancialClock(Start));
        var result = await financial.FinalizeAsync(hold.Id);
        Assert.Equal(unknown ? FinalizationStatus.PendingEvidence : FinalizationStatus.Settled, result.Status);
        if (unknown)
        {
            Assert.Equal(30_000, (await financial.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
            await Usage(services, new MutableFinancialClock(Start)).RecordVerifiedAsync(new(
                hold.RequestId, attemptId, EvidenceSource.Reconciled, 12, 0, 0, null, seed.PriceId, "embed-native"));
            result = await financial.FinalizeAsync(hold.Id);
        }
        Assert.Equal(12, result.Settlement!.Charged.Value);
        Assert.Equal(FinalizationStatus.AlreadyFinalized, (await financial.FinalizeAsync(hold.Id)).Status);
        Assert.Equal(AdmissionStatus.Duplicate, (await financial.ReserveAsync(Admission(seed, 30_000, key, operation: "embeddings"))).Status);
        var state = await financial.GetWalletStateAsync(seed.OrganizationId);
        Assert.Equal(99_988, state!.Wallet.PostedBalance.Value);
        Assert.Equal(0, state.Wallet.ReservedBalance.Value);
        var verifyDb = services.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await verifyDb.Set<BillingLedgerEntryEntity>().CountAsync(entry => entry.Type == "UsageCharge"));
        Assert.Equal("embeddings", (await verifyDb.Set<UsageRequestEntity>().SingleAsync()).Operation);
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => verifyDb.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE catalog.model SET capabilities_json = '[\"Text\"]'::jsonb WHERE id = {seed.ModelId}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
    }

    [Fact]
    public async Task Embeddings_concurrent_duplicate_admission_and_settlement_posts_one_input_charge()
    {
        await ResetAsync();
        var seed = await SeedAsync(embedding: true);
        await CreditAsync(seed.OrganizationId, 100_000);
        var key = Guid.NewGuid().ToString();
        var input = Admission(seed, 30_000, key, operation: "embeddings");
        await using var firstProvider = fixture.CreateServiceProvider();
        await using var firstScope = firstProvider.CreateAsyncScope();
        await using var secondProvider = fixture.CreateServiceProvider();
        await using var secondScope = secondProvider.CreateAsyncScope();
        var first = Financial(firstScope.ServiceProvider, new MutableFinancialClock(Start));
        var second = Financial(secondScope.ServiceProvider, new MutableFinancialClock(Start));
        var results = await Task.WhenAll(first.ReserveAsync(input), second.ReserveAsync(input));
        var admitted = Assert.Single(results, value => value.Status == AdmissionStatus.Reserved);
        Assert.Equal(admitted.RequestId, Assert.Single(results, value => value.Status == AdmissionStatus.Duplicate).RequestId);
        var usage = Usage(firstScope.ServiceProvider, new MutableFinancialClock(Start));
        var attempt = await usage.StartAttemptAsync(admitted.RequestId!.Value, seed.ProviderModelId);
        Assert.True(await usage.MarkDispatchedAsync(attempt.Id));
        await usage.RecordVerifiedAsync(new(admitted.RequestId.Value, attempt.Id, EvidenceSource.Provider, 7, 0, 0, null, seed.PriceId, "embedding-native"));
        var finalized = await Task.WhenAll(first.FinalizeAsync(admitted.Reservation!.Id), second.FinalizeAsync(admitted.Reservation.Id));
        Assert.Equal(7, Assert.Single(finalized, value => value.Status == FinalizationStatus.Settled).Settlement!.Charged.Value);
        Assert.Single(finalized, value => value.Status == FinalizationStatus.AlreadyFinalized);
        Assert.Equal(99_993, (await first.GetWalletStateAsync(seed.OrganizationId))!.Wallet.PostedBalance.Value);
        Assert.Equal(0, (await first.GetWalletStateAsync(seed.OrganizationId))!.Wallet.ReservedBalance.Value);
        var independent = await second.ReserveAsync(Admission(seed, 30_000, key, operation: "responses"));
        Assert.Equal(AdmissionStatus.Reserved, independent.Status);
        Assert.Equal(FinalizationStatus.Released, (await second.ReleaseUndispatchedAsync(independent.Reservation!.Id)).Status);
    }
}
