using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Management.Api;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Notifications.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Modules.Usage.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class CustomerAlertIntegrationTests(PersistenceIntegrationFixture fixture)
{
    private const string WebhookSecret = "test-webhook-secret-0123456789";

    [Fact]
    public async Task Outbound_webhook_destination_is_tenant_scoped_encrypted_show_once_and_audited()
    {
        await ResetAsync();
        var clock = new AlertClock(DateTimeOffset.UtcNow);
        await using var provider = CreateProvider(clock);
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "outbound-owner@example.uz");
        var outsider = await RegisterAsync(scope, "outbound-outsider@example.uz");
        var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var org = await orgs.CreateAsync(owner, "Outbound tenant");
        var other = await orgs.CreateAsync(outsider, "Outbound other");
        var service = scope.ServiceProvider.GetRequiredService<OutboundWebhookService>();
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            service.CreateAsync(outsider, org.Id, "https://alerts.example.com/hook"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(owner, org.Id, "http://alerts.example.com/hook"));
        var destination = await service.CreateAsync(owner, org.Id, "https://alerts.example.com/hook");
        Assert.Equal(64, destination.SigningSecret.Length);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var stored = await db.NotificationDestinations.SingleAsync();
        Assert.Null(stored.EncryptedChatId);
        Assert.Equal("Webhook", stored.Type);
        Assert.DoesNotContain(destination.SigningSecret, Convert.ToHexString(stored.EncryptedWebhookSecret!),
            StringComparison.OrdinalIgnoreCase);
        var protector = scope.ServiceProvider.GetRequiredService<OutboundWebhookSecretProtector>();
        Assert.Equal(Convert.FromHexString(destination.SigningSecret),
            protector.Unprotect(org.Id, destination.Id, stored.EncryptedWebhookSecret!, stored.WebhookKeyVersion!));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(other.Id, destination.Id,
            stored.EncryptedWebhookSecret!, stored.WebhookKeyVersion!));
        await Assert.ThrowsAsync<WebhookDestinationAlreadyExistsException>(() =>
            service.CreateAsync(owner, org.Id, "https://alerts.example.com/other"));
        var list = await scope.ServiceProvider.GetRequiredService<CustomerAlertService>()
            .ListDestinationsAsync(owner, org.Id);
        Assert.Equal("https://alerts.example.com/hook", Assert.Single(list).EndpointUrl);
        Assert.DoesNotContain(destination.SigningSecret, JsonSerializer.Serialize(list), StringComparison.Ordinal);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<CustomerAlertService>()
            .ListDestinationsAsync(outsider, other.Id));
        var audits = await db.Set<AuditEventEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == org.Id).ToArrayAsync();
        Assert.Contains(audits, value => value.Action == "notification.webhook.created");
        Assert.All(audits, value => Assert.DoesNotContain(destination.SigningSecret,
            value.MetadataJson, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Webhook_episode_retries_same_event_then_deduplicates_and_permanent_error_disables()
    {
        await ResetAsync();
        var clock = new AlertClock(DateTimeOffset.UtcNow);
        var sender = new RecordingWebhookSender();
        await using var provider = CreateProvider(clock, webhookSender: sender);
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "outbound-events@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "Webhook episode tenant");
        var destination = await scope.ServiceProvider.GetRequiredService<OutboundWebhookService>()
            .CreateAsync(owner, org.Id, "https://alerts.example.com/hook");
        var alerts = scope.ServiceProvider.GetRequiredService<CustomerAlertService>();
        var rule = await alerts.CreateRuleAsync(owner, org.Id, null, null, destination.Id, "LowBalance", 0);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CustomerAlertEvaluator>().EvaluateDueAsync());
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var message = Assert.Single(await outbox.ClaimAvailableAsync("webhook-test", 20, TimeSpan.FromMinutes(1)),
            value => value.EventType == "customer.alert.webhook");
        var handler = scope.ServiceProvider.GetServices<IOutboxHandler>()
            .Single(value => value.EventType == "customer.alert.webhook");
        sender.TransientNext = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAsync(message, default));
        Assert.Single(sender.Attempts);
        await handler.HandleAsync(message, default);
        await handler.HandleAsync(message, default);
        Assert.Equal(2, sender.Attempts.Count);
        Assert.Equal(sender.Attempts[0].EventId, sender.Attempts[1].EventId);
        Assert.Equal(sender.Attempts[0].Body, sender.Attempts[1].Body);
        Assert.Equal(JsonDocument.Parse(message.Payload).RootElement.GetProperty("EventId").GetGuid(),
            sender.Attempts[1].EventId);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal("Delivered", (await db.CustomerAlertEvents.SingleAsync()).Status);
        Assert.True(await alerts.SetRuleEnabledAsync(owner, org.Id, rule.Id, false));
        Assert.True(await alerts.SetRuleEnabledAsync(owner, org.Id, rule.Id, true));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CustomerAlertEvaluator>().EvaluateDueAsync());
        var next = Assert.Single(await outbox.ClaimAvailableAsync("webhook-test-2", 20, TimeSpan.FromMinutes(1)),
            value => value.EventType == "customer.alert.webhook" && value.Id != message.Id);
        sender.PermanentNext = true;
        await handler.HandleAsync(next, default);
        db.ChangeTracker.Clear();
        Assert.Equal("Suppressed", (await db.CustomerAlertEvents.SingleAsync(value => value.Episode == 2)).Status);
        Assert.Equal("Disabled", (await db.NotificationDestinations.SingleAsync()).Status);
    }

    [Fact]
    public async Task Webhook_management_requires_csrf_enforces_tenant_and_reveals_secret_only_on_create()
    {
        await ResetAsync();
        var clock = new AlertClock(DateTimeOffset.UtcNow);
        await using var app = await CreateAppAsync(clock);
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        await using var scope = app.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var registration = await identity.RegisterAsync("webhook-api-owner@example.uz", "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        var session = (await identity.AuthenticateAsync("webhook-api-owner@example.uz",
            "correct horse battery staple"))!;
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(registration.AccountId, "Outbound API tenant");
        var path = $"/management/v1/organizations/{org.Id}/alerts/webhook/destination";
        var input = new { endpointUrl = "https://alerts.example.com/hook" };
        using (var noCsrf = await client.SendAsync(Request(HttpMethod.Post, path, session, body: input)))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using (var wrongTenant = await client.SendAsync(Request(HttpMethod.Post,
            $"/management/v1/organizations/{Guid.CreateVersion7()}/alerts/webhook/destination",
            session, csrf: true, body: input)))
            Assert.Equal(HttpStatusCode.Forbidden, wrongTenant.StatusCode);
        using (var invalid = await client.SendAsync(Request(HttpMethod.Post, path, session,
            csrf: true, body: new { endpointUrl = "http://localhost/hook" })))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var created = await client.SendAsync(Request(HttpMethod.Post, path, session, csrf: true, body: input));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl?.ToString());
        var first = await created.Content.ReadFromJsonAsync<WebhookDestinationCreated>();
        Assert.NotNull(first);
        using (var duplicate = await client.SendAsync(Request(HttpMethod.Post, path, session,
            csrf: true, body: input)))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var listed = await client.SendAsync(Request(HttpMethod.Get,
            $"/management/v1/organizations/{org.Id}/alerts/destinations", session));
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.DoesNotContain(first.SigningSecret, await listed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using (var disabled = await client.SendAsync(Request(HttpMethod.Delete,
            $"/management/v1/organizations/{org.Id}/alerts/destinations/{first.Id}", session, csrf: true)))
            Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        using var recreated = await client.SendAsync(Request(HttpMethod.Post, path, session,
            csrf: true, body: input));
        Assert.Equal(HttpStatusCode.Created, recreated.StatusCode);
        var second = await recreated.Content.ReadFromJsonAsync<WebhookDestinationCreated>();
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.SigningSecret, second.SigningSecret);
        await app.StopAsync();
    }

    [Fact]
    public async Task Telegram_link_is_one_time_account_bound_and_never_exposes_chat_id()
    {
        await ResetAsync();
        var clock = new AlertClock(DateTimeOffset.UtcNow);
        await using var provider = CreateProvider(clock);
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "alert-owner@example.uz");
        var stranger = await RegisterAsync(scope, "alert-stranger@example.uz");
        var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var org = await orgs.CreateAsync(owner, "Alert tenant");
        var other = await orgs.CreateAsync(stranger, "Other alert tenant");
        var service = scope.ServiceProvider.GetRequiredService<CustomerAlertService>();
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            service.BeginTelegramLinkAsync(stranger, org.Id));
        var link = await service.BeginTelegramLinkAsync(owner, org.Id);
        var token = new Uri(link.DeepLink).Query[7..];
        for (var attempt = 0; attempt < 64 && !token.Contains('-'); attempt++)
        {
            link = await service.BeginTelegramLinkAsync(owner, org.Id);
            token = new Uri(link.DeepLink).Query[7..];
        }
        Assert.Contains('-', token);
        Assert.Equal(43, token.Length);
        Assert.StartsWith("https://t.me/UZLLMTestBot?start=", link.DeepLink);
        Assert.False(await service.CompleteTelegramLinkAsync(token, -1));
        Assert.False(await service.CompleteTelegramLinkAsync(new string('x', 43), 123456));
        Assert.True(await service.CompleteTelegramLinkAsync(token, 123456));
        Assert.False(await service.CompleteTelegramLinkAsync(token, 123456));
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var destination = await db.NotificationDestinations.SingleAsync();
        Assert.Equal(org.Id, destination.OrganizationId);
        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes("123456"), destination.EncryptedChatId);
        Assert.Equal(123456, scope.ServiceProvider.GetRequiredService<TelegramChatProtector>()
            .Unprotect(org.Id, destination.Id, destination.EncryptedChatId!, destination.KeyVersion!));
        var audit = await db.Set<AuditEventEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == org.Id).ToArrayAsync();
        Assert.Contains(audit, value => value.Action == "notification.telegram.connected");
        Assert.All(audit, value =>
        {
            Assert.DoesNotContain(token, value.MetadataJson, StringComparison.Ordinal);
            Assert.DoesNotContain("123456", value.MetadataJson, StringComparison.Ordinal);
        });
        Assert.ThrowsAny<CryptographicException>(() => scope.ServiceProvider.GetRequiredService<TelegramChatProtector>()
            .Unprotect(other.Id, destination.Id, destination.EncryptedChatId!, destination.KeyVersion!));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateRuleAsync(stranger, other.Id,
            null, null, destination.Id, "LowBalance", 0));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => service.CreateRuleAsync(stranger, org.Id,
            null, null, destination.Id, "LowBalance", 0));
        var expired = await service.BeginTelegramLinkAsync(owner, org.Id);
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.False(await service.CompleteTelegramLinkAsync(new Uri(expired.DeepLink).Query[7..], 999));
        var first = new Uri((await service.BeginTelegramLinkAsync(owner, org.Id)).DeepLink).Query[7..];
        var second = new Uri((await service.BeginTelegramLinkAsync(owner, org.Id)).DeepLink).Query[7..];
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var results = await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<CustomerAlertService>()
                .CompleteTelegramLinkAsync(first, 111),
            secondScope.ServiceProvider.GetRequiredService<CustomerAlertService>()
                .CompleteTelegramLinkAsync(second, 222));
        Assert.All(results, Assert.True);
        Assert.Single(await db.NotificationDestinations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Threshold_episodes_deduplicate_rearm_and_delivery_retries_without_second_send()
    {
        await ResetAsync();
        var clock = new AlertClock(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        await using var provider = CreateProvider(clock, sender);
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "episode-owner@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "Episode tenant");
        var service = scope.ServiceProvider.GetRequiredService<CustomerAlertService>();
        var token = new Uri((await service.BeginTelegramLinkAsync(owner, org.Id)).DeepLink).Query[7..];
        Assert.Equal(43, token.Length);
        var challenge = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>()
            .TelegramLinkChallenges.SingleAsync();
        Assert.Equal(SHA256.HashData(Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=")),
            challenge.TokenHash);
        Assert.True(challenge.ExpiresAt > clock.GetUtcNow());
        Assert.True(await service.CompleteTelegramLinkAsync(token, 43210));
        var destination = Assert.Single(await service.ListDestinationsAsync(owner, org.Id));
        var rule = await service.CreateRuleAsync(owner, org.Id, null, null,
            destination.Id, "LowBalance", 0);
        var evaluator = scope.ServiceProvider.GetRequiredService<CustomerAlertEvaluator>();
        Assert.Equal(1, await evaluator.EvaluateDueAsync());
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await evaluator.EvaluateDueAsync());
        var wallet = scope.ServiceProvider.GetRequiredService<IWalletLedgerService>();
        Assert.Equal(LedgerPostingStatus.Posted, (await wallet.PostAsync(new LedgerPostingInput(org.Id,
            LedgerEntryType.TopUp, new SignedUsdMicroAmount(100), "test", Guid.CreateVersion7()))).Status);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await evaluator.EvaluateDueAsync());
        Assert.Equal(LedgerPostingStatus.Posted, (await wallet.PostAsync(new LedgerPostingInput(org.Id,
            LedgerEntryType.AdjustmentDebit, new SignedUsdMicroAmount(-100), "test", Guid.CreateVersion7()))).Status);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await evaluator.EvaluateDueAsync());
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(2, await db.CustomerAlertEvents.CountAsync(value => value.RuleId == rule.Id));
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var messages = (await outbox.ClaimAvailableAsync("alert-test", 20, TimeSpan.FromMinutes(1)))
            .Where(value => value.EventType == "customer.alert.telegram").ToArray();
        Assert.Equal(2, messages.Length);
        var handler = scope.ServiceProvider.GetServices<IOutboxHandler>()
            .Single(value => value.EventType == "customer.alert.telegram");
        sender.FailNext = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAsync(messages[0], default));
        Assert.Empty(sender.Messages);
        await handler.HandleAsync(messages[0], default);
        await handler.HandleAsync(messages[0], default);
        Assert.True(await outbox.MarkProcessedAsync(messages[0].Id, "alert-test"));
        Assert.Single(sender.Messages);
        await handler.HandleAsync(messages[1], default);
        Assert.True(await outbox.MarkProcessedAsync(messages[1].Id, "alert-test"));
        Assert.Equal(2, sender.Messages.Count);
        Assert.All(await db.CustomerAlertEvents.ToArrayAsync(), value => Assert.Equal("Delivered", value.Status));
        Assert.True(await service.SetRuleEnabledAsync(owner, org.Id, rule.Id, false));
        Assert.True(await service.SetRuleEnabledAsync(owner, org.Id, rule.Id, true));
        sender.PermanentNext = true;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await evaluator.EvaluateDueAsync());
        var next = (await outbox.ClaimAvailableAsync("alert-test-2", 20, TimeSpan.FromMinutes(1)))
            .Single(value => value.EventType == "customer.alert.telegram");
        await handler.HandleAsync(next, default);
        Assert.Equal("Suppressed", (await db.CustomerAlertEvents.SingleAsync(value => value.Id ==
            JsonDocument.Parse(next.Payload).RootElement.GetProperty("EventId").GetGuid())).Status);
        db.ChangeTracker.Clear();
        Assert.Equal("Disabled", (await db.NotificationDestinations.SingleAsync()).Status);
        Assert.True(await service.DisableDestinationAsync(owner, org.Id, destination.Id));
        Assert.Equal(0, await evaluator.EvaluateDueAsync());
    }

    [Fact]
    public async Task Budget_warning_uses_current_window_and_telegram_webhook_requires_secret_private_chat()
    {
        await ResetAsync();
        var clock = new AlertClock(DateTimeOffset.UtcNow);
        await using var app = await CreateAppAsync(clock);
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        await using var scope = app.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var registration = await identity.RegisterAsync("webhook-owner@example.uz", "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        var session = (await identity.AuthenticateAsync("webhook-owner@example.uz", "correct horse battery staple"))!;
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(registration.AccountId, "Webhook tenant");
        var project = await scope.ServiceProvider.GetRequiredService<IProjectService>()
            .CreateAsync(registration.AccountId, org.Id, "Alerts project");
        var linkPath = $"/management/v1/organizations/{org.Id}/alerts/telegram/link";
        using (var noCsrf = await client.SendAsync(Request(HttpMethod.Post, linkPath, session)))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var linked = await client.SendAsync(Request(HttpMethod.Post, linkPath, session, csrf: true));
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        var link = await linked.Content.ReadFromJsonAsync<TelegramLink>();
        Assert.NotNull(link);
        var token = new Uri(link.DeepLink).Query[7..];
        Assert.Equal(43, token.Length);
        var update = new { update_id = 1, message = new { text = "/start " + token,
            chat = new { id = 87654L, type = "private" } } };
        using (var unsigned = await client.PostAsJsonAsync("/integrations/telegram/webhook", update))
            Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using (var group = await TelegramUpdateAsync(client, new { message = new { text = "/start " + token,
            chat = new { id = 87654L, type = "group" } } }))
            Assert.Equal(HttpStatusCode.OK, group.StatusCode);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Null((await db.TelegramLinkChallenges.SingleAsync()).ConsumedAt);
        using (var accepted = await TelegramUpdateAsync(client, update))
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        db.ChangeTracker.Clear();
        Assert.NotNull((await db.TelegramLinkChallenges.SingleAsync()).ConsumedAt);
        using (var replay = await TelegramUpdateAsync(client, update))
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<CustomerAlertService>()
            .ListDestinationsAsync(registration.AccountId, org.Id));
        var policy = await scope.ServiceProvider.GetRequiredService<IFinancialService>()
            .SetBudgetAsync(org.Id, project.Id, null, BudgetPeriod.Daily, new UsdMicroAmount(100));
        Assert.NotNull(policy);
        await db.Set<BillingBudgetBucketEntity>().Where(value => value.PolicyId == policy.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.CapturedMicroUsd, 80));
        var destination = Assert.Single(await scope.ServiceProvider.GetRequiredService<CustomerAlertService>()
            .ListDestinationsAsync(registration.AccountId, org.Id));
        var rulesPath = $"/management/v1/organizations/{org.Id}/alerts/rules";
        var ruleInput = new { type = "BudgetWarning", threshold = 8000, projectId = project.Id,
            budgetPolicyId = policy.Id, destinationId = destination.Id };
        using (var noCsrfRule = await client.SendAsync(Request(HttpMethod.Post, rulesPath,
            session, body: ruleInput)))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrfRule.StatusCode);
        using (var wrongTenantRule = await client.SendAsync(Request(HttpMethod.Post,
            $"/management/v1/organizations/{Guid.CreateVersion7()}/alerts/rules",
            session, csrf: true, body: ruleInput)))
            Assert.Equal(HttpStatusCode.Forbidden, wrongTenantRule.StatusCode);
        using var created = await client.SendAsync(Request(HttpMethod.Post, rulesPath,
            session, csrf: true, body: ruleInput));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var rule = await created.Content.ReadFromJsonAsync<AlertRuleView>();
        Assert.NotNull(rule);
        using var listed = await client.SendAsync(Request(HttpMethod.Get, rulesPath, session));
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Single((await listed.Content.ReadFromJsonAsync<AlertRuleView[]>())!);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CustomerAlertEvaluator>().EvaluateDueAsync());
        Assert.Equal(8000, (await db.CustomerAlertEvents.SingleAsync(value => value.RuleId == rule.Id)).ObservedValue);
        await app.StopAsync();
    }

    [Fact]
    public async Task Error_spike_requires_minimum_sample_and_rearms_after_recovery()
    {
        await ResetAsync();
        var clock = new AlertClock(DateTimeOffset.UtcNow);
        await using var provider = CreateProvider(clock);
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "error-owner@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "Error tenant");
        var project = await scope.ServiceProvider.GetRequiredService<IProjectService>()
            .CreateAsync(owner, org.Id, "Error project");
        var service = scope.ServiceProvider.GetRequiredService<CustomerAlertService>();
        var token = new Uri((await service.BeginTelegramLinkAsync(owner, org.Id)).DeepLink).Query[7..];
        Assert.Equal(43, token.Length);
        Assert.True(await service.CompleteTelegramLinkAsync(token, 24680));
        var destination = Assert.Single(await service.ListDestinationsAsync(owner, org.Id));
        var rule = await service.CreateRuleAsync(owner, org.Id, project.Id, null,
            destination.Id, "ErrorSpike", 3000);
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var modelId = Guid.CreateVersion7();
        var keyId = Guid.CreateVersion7();
        db.Set<CatalogModelEntity>().Add(new CatalogModelEntity
        {
            Id = modelId, CanonicalCode = "alert-test-model", DisplayName = "Alert test model",
            ContextLength = 8192, MaxOutputTokens = 1024, CapabilitiesJson = "{}",
            Status = "Active", CreatedAt = clock.GetUtcNow()
        });
        db.Set<GatewayApiKeyEntity>().Add(new GatewayApiKeyEntity
        {
            Id = keyId, ProjectId = project.Id, Name = "Alert test key", Prefix = "uz_alert01",
            SecretFingerprint = new byte[32], Status = "Active", CreatedByAccountId = owner,
            CreatedAt = clock.GetUtcNow()
        });
        await db.SaveChangesAsync();
        var evaluator = scope.ServiceProvider.GetRequiredService<CustomerAlertEvaluator>();
        SeedRequests(db, org.Id, project.Id, keyId, modelId, clock.GetUtcNow(), 10, 9);
        await db.SaveChangesAsync();
        Assert.Equal(0, await evaluator.EvaluateDueAsync());
        clock.Advance(TimeSpan.FromMinutes(1));
        SeedRequests(db, org.Id, project.Id, keyId, modelId, clock.GetUtcNow(), 10, 1);
        await db.SaveChangesAsync();
        Assert.Equal(1, await evaluator.EvaluateDueAsync());
        Assert.Equal(5000, (await db.CustomerAlertEvents.SingleAsync(value => value.RuleId == rule.Id)).ObservedValue);
        clock.Advance(TimeSpan.FromMinutes(1));
        SeedRequests(db, org.Id, project.Id, keyId, modelId, clock.GetUtcNow(), 20, 0);
        await db.SaveChangesAsync();
        Assert.Equal(0, await evaluator.EvaluateDueAsync());
        clock.Advance(TimeSpan.FromMinutes(1));
        SeedRequests(db, org.Id, project.Id, keyId, modelId, clock.GetUtcNow(), 20, 20);
        await db.SaveChangesAsync();
        Assert.Equal(1, await evaluator.EvaluateDueAsync());
        Assert.Equal(2, await db.CustomerAlertEvents.CountAsync(value => value.RuleId == rule.Id));
    }

    private static void SeedRequests(FoundationDbContext db, Guid organizationId, Guid projectId,
        Guid keyId, Guid modelId, DateTimeOffset now, int count, int failures)
    {
        for (var index = 0; index < count; index++)
            db.Set<UsageRequestEntity>().Add(new UsageRequestEntity
            {
                Id = Guid.CreateVersion7(), OrganizationId = organizationId, ProjectId = projectId,
                ApiKeyId = keyId, CanonicalModelId = modelId, StartedAt = now.AddSeconds(-1),
                CompletedAt = now, ExecutionState = index < failures ? "Failed" : "Succeeded",
                DeliveryState = "Completed", FinancialState = "Released", IsStream = false,
                Operation = "chat.completions", HttpStatus = index < failures ? 503 : 200
            });
    }

    private async Task ResetAsync()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
    }

    private ServiceProvider CreateProvider(AlertClock clock, RecordingSender? sender = null,
        RecordingWebhookSender? webhookSender = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        var configuration = Configuration();
        services.AddUzllmPersistence(configuration).AddUzllmIdentity().AddUzllmAudit().AddUzllmOrganizations()
            .AddUzllmProjects().AddUzllmUsage().AddUzllmBilling().AddUzllmCustomerAlerts(configuration);
        if (sender is not null) services.AddSingleton<ITelegramMessageSender>(sender);
        services.AddSingleton<IWebhookAddressResolver>(new FixedWebhookResolver());
        if (webhookSender is not null) services.AddSingleton<IOutboundWebhookSender>(webhookSender);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private async Task<WebApplication> CreateAppAsync(AlertClock clock)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddConfiguration(Configuration());
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddUzllmPersistence(builder.Configuration).AddUzllmIdentity().AddUzllmAudit()
            .AddUzllmOrganizations().AddUzllmProjects().AddUzllmUsage().AddUzllmBilling()
            .AddUzllmCustomerAlerts(builder.Configuration);
        builder.Services.AddSingleton<IWebhookAddressResolver>(new FixedWebhookResolver());
        var app = builder.Build();
        app.UseUzllmManagementSession();
        app.MapUzllmCustomerAlertEndpoints();
        await app.StartAsync();
        return app;
    }

    private IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString,
        ["Telegram:BotUsername"] = "UZLLMTestBot",
        ["Telegram:WebhookSecret"] = WebhookSecret,
        ["Telegram:ActiveChatKeyVersion"] = "test-v1",
        ["Telegram:ChatKeys:test-v1"] = Convert.ToBase64String(new byte[32])
        , ["OutboundWebhooks:ActiveKeyVersion"] = "test-v1"
        , ["OutboundWebhooks:Keys:test-v1"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())
    }).Build();

    private static async Task<Guid> RegisterAsync(AsyncServiceScope scope, string email)
    {
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var registration = await identity.RegisterAsync(email, "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        return registration.AccountId;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, BrowserSessionTokens session,
        bool csrf = false, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{IdentityCookieNames.Session}={session.SessionToken}; " +
            $"{IdentityCookieNames.Csrf}={session.CsrfToken}");
        if (csrf) request.Headers.Add(IdentityCookieNames.CsrfHeader, session.CsrfToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<HttpResponseMessage> TelegramUpdateAsync(HttpClient client, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/integrations/telegram/webhook")
        { Content = JsonContent.Create(payload) };
        Assert.Contains("\"message\"", await request.Content.ReadAsStringAsync());
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", WebhookSecret);
        return await client.SendAsync(request);
    }

    private sealed class AlertClock(DateTimeOffset instant) : TimeProvider
    {
        private DateTimeOffset current = instant;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }

    private sealed class RecordingSender : ITelegramMessageSender
    {
        public bool FailNext { get; set; }
        public bool PermanentNext { get; set; }
        public List<(long ChatId, string Text)> Messages { get; } = [];
        public Task SendAsync(long chatId, string message, CancellationToken cancellationToken = default)
        {
            if (FailNext) { FailNext = false; throw new HttpRequestException("Test transport failure."); }
            if (PermanentNext) { PermanentNext = false; throw new TelegramDestinationUnavailableException(); }
            Messages.Add((chatId, message));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedWebhookResolver : IWebhookAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { IPAddress.Parse("1.1.1.1") });
    }

    private sealed class RecordingWebhookSender : IOutboundWebhookSender
    {
        public bool TransientNext { get; set; }
        public bool PermanentNext { get; set; }
        public List<(Guid EventId, byte[] Body)> Attempts { get; } = [];
        public Task SendAsync(Uri endpoint, byte[] secret, Guid eventId, byte[] body,
            CancellationToken cancellationToken)
        {
            Attempts.Add((eventId, body));
            if (TransientNext) { TransientNext = false; throw new HttpRequestException("Transient fixture."); }
            if (PermanentNext) { PermanentNext = false; throw new OutboundWebhookPermanentFailureException(); }
            return Task.CompletedTask;
        }
    }
}
