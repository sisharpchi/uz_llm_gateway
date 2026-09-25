using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Organizations.Application;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Payments.Application;
using UZLLM.Modules.Payments.Contracts;
using UZLLM.Modules.Payments.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class PaymentIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private static readonly PaymentConfiguration Config = new(100, 0, "payme-test-merchant",
        "payme-test-key", "click-test-merchant", "click-test-service", "click-test-secret");

    [Fact]
    public async Task PAY_001_three_Payme_Perform_callbacks_credit_wallet_once()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Payme, new UzsTiyinAmount(100_000));
        Assert.Equal(1_000, quote.Fee.Value);
        Assert.Equal(99_000, quote.Credit.Value);
        var result = await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId, quote.Id, "key-1");
        Assert.False(result.Duplicate);
        Assert.NotNull(result.CheckoutUrl);
        var api = new PaymeMerchantApi(service, Config);
        var auth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("Paycom:payme-test-key"));
        var create = await api.HandleAsync(auth, JsonSerializer.Serialize(new
        {
            id = 1, method = "CreateTransaction",
            @params = new { id = "payme-tx-1", time = 1000, amount = 100_000,
                account = new { intent_id = result.Intent.Id.ToString("D") } }
        }));
        Assert.Null(create.Error);
        var perform = """{"id":2,"method":"PerformTransaction","params":{"id":"payme-tx-1"}}""";
        for (var i = 0; i < 3; i++)
            Assert.Null((await api.HandleAsync(auth, perform)).Error);

        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<BillingLedgerEntryEntity>().CountAsync(item =>
            item.ReferenceType == "payment_intent" && item.ReferenceId == result.Intent.Id
            && item.Type == "TopUp"));
        var wallet = await new PostgreSqlWalletLedgerStore(db).FindWalletAsync(seed.OrganizationId);
        Assert.Equal(quote.Credit.Value, wallet!.PostedBalance.Value);
        Assert.Equal(PaymentStatus.Paid, (await service.GetIntentAsync(seed.AccountId,
            seed.OrganizationId, result.Intent.Id))!.Status);
        Assert.Equal(1, await db.Set<PaymentIntentEntity>().CountAsync(item => item.Id == result.Intent.Id));
    }

    [Fact]
    public async Task PAY_002_other_external_transaction_cannot_claim_paid_intent()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Payme, new UzsTiyinAmount(100_000));
        var intent = (await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId, quote.Id, "key-2")).Intent;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("fixture"));
        Assert.Equal(PaymentCommandStatus.Accepted, (await service.PrepareAsync(PaymentProvider.Payme,
            intent.Id, "tx-1", intent.Amount, 1000, "create:1", hash)).Status);
        Assert.Equal(PaymentCommandStatus.Accepted, (await service.CompleteAsync(PaymentProvider.Payme,
            "tx-1", null, null, "perform:1", hash)).Status);
        Assert.Equal(PaymentCommandStatus.Conflict, (await service.PrepareAsync(PaymentProvider.Payme,
            intent.Id, "tx-2", intent.Amount, 1001, "create:2", hash)).Status);
        Assert.Equal(PaymentCommandStatus.NotFound, (await service.CompleteAsync(PaymentProvider.Payme,
            "tx-2", null, null, "perform:2", hash)).Status);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<BillingLedgerEntryEntity>().CountAsync(item =>
            item.ReferenceType == "payment_intent" && item.ReferenceId == intent.Id));
    }

    [Fact]
    public async Task PAY_003_paid_reversal_preserves_reserved_balance_and_creates_recovery_debt()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Payme, new UzsTiyinAmount(100_000));
        var intent = (await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId, quote.Id, "key-3")).Intent;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("fixture"));
        await service.PrepareAsync(PaymentProvider.Payme, intent.Id, "tx-3", intent.Amount,
            1000, "create:3", hash);
        await service.CompleteAsync(PaymentProvider.Payme, "tx-3", null, null, "perform:3", hash);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        // A pre-existing hold must survive a payment reversal; the uncovered amount becomes debt.
        var reserved = quote.Credit.Value / 2;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE billing.wallet SET reserved_balance_micro_usd = {reserved} WHERE organization_id = {seed.OrganizationId}");
        var canceled = await service.CancelAsync(PaymentProvider.Payme, "tx-3", null, null,
            5, "cancel:3", hash);
        Assert.Equal(PaymentCommandStatus.Accepted, canceled.Status);
        Assert.Equal(PaymentStatus.Canceled, canceled.Intent!.Status);
        Assert.Equal(PaymentCommandStatus.Duplicate, (await service.CancelAsync(PaymentProvider.Payme,
            "tx-3", null, null, 5, "cancel:3", hash)).Status);
        var wallet = await new PostgreSqlWalletLedgerStore(db).FindWalletAsync(seed.OrganizationId);
        Assert.Equal(reserved, wallet!.ReservedBalance.Value);
        Assert.True(wallet.PostedBalance.Value >= reserved);
        Assert.Equal(1, await db.Set<BillingReversalEntity>().CountAsync(item =>
            item.ExternalReferenceId == intent.Id));
        Assert.Equal(1, await db.Set<BillingRecoveryDebtEntity>().CountAsync(item =>
            item.OrganizationId == seed.OrganizationId));
    }

    [Fact]
    public async Task CLICK_documented_signature_order_prepare_complete_and_invalid_inputs()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Click, new UzsTiyinAmount(100_000));
        var intent = (await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId, quote.Id, "click-key")).Intent;
        var api = new ClickShopApi(service, Config);
        var prepare = ClickFields(intent.Id, "0", "1000.00", null, "0");
        Assert.True(ClickSignature.Verify(prepare, Config.ClickSecretKey!));
        var prepared = await api.HandleAsync(prepare, Serialize(prepare), 0);
        Assert.Equal(0, prepared.Error);
        Assert.True(prepared.MerchantPrepareId > 0);
        var complete = ClickFields(intent.Id, "1", "1000.00", prepared.MerchantPrepareId, "0");
        Assert.Equal(0, (await api.HandleAsync(complete, Serialize(complete), 1)).Error);
        Assert.Equal(-4, (await api.HandleAsync(complete, Serialize(complete), 1)).Error);
        var badSignature = new Dictionary<string, string>(complete) { ["sign_string"] = new string('0', 32) };
        Assert.Equal(-1, (await api.HandleAsync(badSignature, Serialize(badSignature), 1)).Error);
        var badAmount = ClickFields(intent.Id, "1", "999.00", prepared.MerchantPrepareId, "0");
        Assert.Equal(-2, (await api.HandleAsync(badAmount, Serialize(badAmount), 1)).Error);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<BillingLedgerEntryEntity>().CountAsync(item =>
            item.ReferenceType == "payment_intent" && item.ReferenceId == intent.Id));
    }

    [Fact]
    public async Task Reconciliation_marks_stale_created_payment_for_manual_verification_without_credit()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Payme, new UzsTiyinAmount(100_000));
        var intent = (await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId, quote.Id, "stale-key")).Intent;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("fixture"));
        await service.PrepareAsync(PaymentProvider.Payme, intent.Id, "stale-tx", intent.Amount,
            1000, "create:stale", hash);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE payment.payment_intent SET bound_at = {DateTimeOffset.UtcNow.AddDays(-1)} WHERE id = {intent.Id}");
        var first = await service.ReconcileAsync(intent.Id);
        var second = await service.ReconcileAsync(intent.Id);
        Assert.Equal("ProviderStatusUnverified", first.Outcome);
        Assert.True(first.CaseCreated);
        Assert.False(second.CaseCreated);
        Assert.Equal(0, await db.Set<BillingLedgerEntryEntity>().CountAsync(item => item.ReferenceId == intent.Id));
    }

    [Fact]
    public async Task Payme_documented_methods_reject_bad_auth_and_amount_and_report_statement_and_cancellation()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Payme, new UzsTiyinAmount(100_000));
        var intent = (await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId, quote.Id, "payme-methods")).Intent;
        var api = new PaymeMerchantApi(service, Config);
        var auth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("Paycom:payme-test-key"));
        var account = new { intent_id = intent.Id.ToString("D") };
        var check = JsonSerializer.Serialize(new { id = 1, method = "CheckPerformTransaction",
            @params = new { amount = 100_000, account } });
        Assert.Equal(-32504, (await api.HandleAsync("Basic invalid", check)).Error!.Code);
        Assert.Null((await api.HandleAsync(auth, check)).Error);
        var wrongAmount = JsonSerializer.Serialize(new { id = 2, method = "CheckPerformTransaction",
            @params = new { amount = 99_999, account } });
        Assert.Equal(-31001, (await api.HandleAsync(auth, wrongAmount)).Error!.Code);
        var create = JsonSerializer.Serialize(new { id = 3, method = "CreateTransaction",
            @params = new { id = "payme-method-tx", time = 1_000L, amount = 100_000, account } });
        Assert.Null((await api.HandleAsync(auth, create)).Error);
        Assert.Null((await api.HandleAsync(auth, create)).Error);
        var status = JsonSerializer.Serialize(new { id = 4, method = "CheckTransaction",
            @params = new { id = "payme-method-tx" } });
        using (var result = JsonDocument.Parse(JsonSerializer.Serialize((await api.HandleAsync(auth, status)).Result)))
            Assert.Equal(1, result.RootElement.GetProperty("state").GetInt32());
        var statement = JsonSerializer.Serialize(new { id = 5, method = "GetStatement",
            @params = new { from = 0L, to = 2_000L } });
        using (var result = JsonDocument.Parse(JsonSerializer.Serialize((await api.HandleAsync(auth, statement)).Result)))
            Assert.Single(result.RootElement.GetProperty("transactions").EnumerateArray());
        var cancel = JsonSerializer.Serialize(new { id = 6, method = "CancelTransaction",
            @params = new { id = "payme-method-tx", reason = 5 } });
        Assert.Null((await api.HandleAsync(auth, cancel)).Error);
        Assert.Null((await api.HandleAsync(auth, cancel)).Error);
        using (var result = JsonDocument.Parse(JsonSerializer.Serialize((await api.HandleAsync(auth, status)).Result)))
            Assert.Equal(-1, result.RootElement.GetProperty("state").GetInt32());
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(0, await db.Set<BillingLedgerEntryEntity>().CountAsync(item => item.ReferenceId == intent.Id));
    }

    [Fact]
    public async Task Concurrent_complete_replay_across_scopes_posts_exactly_one_credit()
    {
        var seed = await SeedAsync();
        Guid intentId;
        await using (var provider = fixture.CreateServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = Service(scope.ServiceProvider);
            var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
                PaymentProvider.Payme, new UzsTiyinAmount(100_000));
            var intent = (await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId, quote.Id,
                "concurrent-replay")).Intent;
            intentId = intent.Id;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes("fixture"));
            Assert.Equal(PaymentCommandStatus.Accepted, (await service.PrepareAsync(PaymentProvider.Payme,
                intentId, "concurrent-tx", intent.Amount, 1000, "create:concurrent", hash)).Status);
        }
        async Task<PaymentCommandStatus> CompleteOneAsync(int index)
        {
            await using var provider = fixture.CreateServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"perform:{index}"));
            var result = await Service(scope.ServiceProvider).CompleteAsync(PaymentProvider.Payme,
                "concurrent-tx", null, null, $"perform:{index}", hash);
            return result.Status;
        }
        var statuses = await Task.WhenAll(Enumerable.Range(0, 3).Select(CompleteOneAsync));
        Assert.Single(statuses, status => status == PaymentCommandStatus.Accepted);
        Assert.Equal(2, statuses.Count(status => status == PaymentCommandStatus.Duplicate));
        await using var verifier = fixture.CreateServiceProvider();
        await using var verifyScope = verifier.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<BillingLedgerEntryEntity>().CountAsync(item =>
            item.ReferenceType == "payment_intent" && item.ReferenceId == intentId));
    }

    [Fact]
    public void CLICK_public_protocol_signature_fixture_matches_fixed_known_digest()
    {
        var fields = ClickFields(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "0", "1000.00", null, "0");
        Assert.Equal("0924b2ab999ab10e46bbaf628ac275cf", fields["sign_string"]);
        Assert.True(ClickSignature.Verify(fields, "click-test-secret"));
        fields["amount"] = "1000.01";
        Assert.False(ClickSignature.Verify(fields, "click-test-secret"));
    }

    [Fact]
    public async Task Tenant_cannot_create_or_read_another_organizations_payment()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Payme, new UzsTiyinAmount(100_000));
        var otherOrganizationId = Guid.CreateVersion7();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateIntentAsync(
            seed.AccountId, otherOrganizationId, quote.Id, "wrong-org"));
        var intent = (await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId,
            quote.Id, "right-org")).Intent;
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => service.GetIntentAsync(
            Guid.CreateVersion7(), seed.OrganizationId, intent.Id));
        Assert.Null(await service.GetIntentAsync(seed.AccountId, seed.OrganizationId, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Topup_intent_replay_is_stable_and_key_reuse_for_another_quote_conflicts()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var firstQuote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Payme, new UzsTiyinAmount(100_000));
        var secondQuote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Payme, new UzsTiyinAmount(200_000));
        var first = await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId,
            firstQuote.Id, "stable-key");
        var replay = await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId,
            firstQuote.Id, "stable-key");
        Assert.Equal(first.Intent.Id, replay.Intent.Id);
        Assert.True(replay.Duplicate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateIntentAsync(
            seed.AccountId, seed.OrganizationId, secondQuote.Id, "stable-key"));
    }

    [Fact]
    public async Task CLICK_negative_Complete_reverses_a_previously_paid_intent_once()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Click, new UzsTiyinAmount(100_000));
        var intent = (await service.CreateIntentAsync(seed.AccountId, seed.OrganizationId, quote.Id,
            "click-reverse")).Intent;
        var api = new ClickShopApi(service, Config);
        var prepare = ClickFields(intent.Id, "0", "1000.00", null, "0");
        var prepareId = (await api.HandleAsync(prepare, Serialize(prepare), 0)).MerchantPrepareId;
        Assert.NotNull(prepareId);
        var complete = ClickFields(intent.Id, "1", "1000.00", prepareId, "0");
        Assert.Equal(0, (await api.HandleAsync(complete, Serialize(complete), 1)).Error);
        var reversal = ClickFields(intent.Id, "1", "1000.00", prepareId, "-1");
        Assert.Equal(-9, (await api.HandleAsync(reversal, Serialize(reversal), 1)).Error);
        Assert.Equal(-9, (await api.HandleAsync(reversal, Serialize(reversal), 1)).Error);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Set<BillingReversalEntity>().CountAsync(item =>
            item.ExternalReferenceId == intent.Id));
        Assert.Equal(PaymentStatus.Canceled, (await service.GetIntentAsync(seed.AccountId,
            seed.OrganizationId, intent.Id))!.Status);
    }

    [Fact]
    public async Task Payment_quote_is_immutable_and_runtime_role_can_allocate_CLICK_prepare_id()
    {
        var seed = await SeedAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = Service(scope.ServiceProvider);
        var quote = await service.CreateQuoteAsync(seed.AccountId, seed.OrganizationId,
            PaymentProvider.Click, new UzsTiyinAmount(100_000));
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payment.fx_quote SET amount_tiyin = {100_001L} WHERE id = {quote.Id}"));
        await using var connection = new NpgsqlConnection(fixture.RuntimeConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT nextval('payment.click_prepare_id_seq')";
        Assert.True(Convert.ToInt64(await command.ExecuteScalarAsync()) > 0);
    }

    private async Task<(Guid AccountId, Guid OrganizationId)> SeedAsync()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        await using var provider = fixture.CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var accountId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        db.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
        {
            Id = accountId, Email = $"payment-{accountId:N}@example.uz", PasswordHash = "not-a-password",
            Status = "Active", CreatedAt = now, UpdatedAt = now
        });
        db.Set<OrganizationEntity>().Add(new OrganizationEntity
        {
            Id = organizationId, Name = "Payment tenant", Status = "Active", CreatedAt = now
        });
        db.Set<OrganizationMemberEntity>().Add(new OrganizationMemberEntity
        {
            OrganizationId = organizationId, AccountId = accountId,
            Role = "Owner", Status = "Active", CreatedAt = now
        });
        db.Set<BillingFxRateSnapshotEntity>().Add(new BillingFxRateSnapshotEntity
        {
            Id = Guid.CreateVersion7(), Source = "operator-test", UzsTiyinPerUsd = 1_000_000m,
            ObservedAt = now
        });
        await db.SaveChangesAsync();
        return (accountId, organizationId);
    }

    private static PaymentService Service(IServiceProvider services)
    {
        var db = services.GetRequiredService<FoundationDbContext>();
        return new PaymentService(new PostgreSqlPaymentStore(db),
            new OrganizationAuthorizationService(new PostgreSqlOrganizationStore(db)),
            new PostgreSqlWalletLedgerStore(db), new PostgreSqlFinancialStore(db),
            services.GetRequiredService<ITransactionCoordinator>(),
            services.GetRequiredService<ILeasedJobStore>(),
            services.GetRequiredService<IOutboxStore>(),
            services.GetRequiredService<IOperationalAlertPublisher>(), Config, TimeProvider.System);
    }

    // CLICK official Shop API examples use this exact concatenation, with the Prepare ID
    // included only for action=1. The signature constants are an independent fixture.
    private static Dictionary<string, string> ClickFields(Guid intentId, string action,
        string amount, int? prepareId, string error)
    {
        var fields = new Dictionary<string, string>
        {
            ["click_trans_id"] = "90001", ["service_id"] = "click-test-service",
            ["merchant_trans_id"] = intentId.ToString("D"), ["amount"] = amount,
            ["action"] = action, ["error"] = error, ["error_note"] = "Success",
            ["sign_time"] = "2026-09-25 10:00:00", ["click_paydoc_id"] = "80001"
        };
        if (prepareId is not null) fields["merchant_prepare_id"] = prepareId.Value.ToString();
        var signed = "90001click-test-serviceclick-test-secret" + intentId.ToString("D")
            + (action == "1" ? prepareId!.Value.ToString() : "")
            + amount + action + "2026-09-25 10:00:00";
        fields["sign_string"] = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
        return fields;
    }

    private static string Serialize(IReadOnlyDictionary<string, string> fields) =>
        string.Join('&', fields.Select(item => Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value)));
}
