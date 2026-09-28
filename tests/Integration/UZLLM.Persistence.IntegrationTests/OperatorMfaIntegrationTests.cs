using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class OperatorMfaIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task PostgreSql_Mfa_confirmation_replay_and_audited_recovery_are_atomic_across_scopes()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
        try
        {
            var clock = new MfaClock(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString }).Build();
            await using var services = new ServiceCollection().AddUzllmPersistence(configuration)
                .AddUzllmIdentity().AddSingleton<TimeProvider>(clock)
                .BuildServiceProvider(validateScopes: true);

            Guid targetId;
            Guid actorId;
            BrowserSessionTokens targetSession;
            BrowserSessionTokens targetSecondSession;
            BrowserSessionTokens actorSession;
            string targetSecret;
            await using (var setupScope = services.CreateAsyncScope())
            {
                var identity = setupScope.ServiceProvider.GetRequiredService<IIdentityService>();
                var target = await identity.RegisterAsync("mfa-target@example.uz", "correct horse battery staple");
                var actor = await identity.RegisterAsync("mfa-actor@example.uz", "correct horse battery staple");
                Assert.True(await identity.VerifyEmailAsync(target.VerificationToken));
                Assert.True(await identity.VerifyEmailAsync(actor.VerificationToken));
                targetId = target.AccountId;
                actorId = actor.AccountId;
                await identity.GrantOperatorAccessAsync(targetId);
                await identity.GrantOperatorAccessAsync(actorId);
                targetSession = (await identity.AuthenticateAsync("mfa-target@example.uz", "correct horse battery staple"))!;
                targetSecondSession = (await identity.AuthenticateAsync("mfa-target@example.uz", "correct horse battery staple"))!;
                actorSession = (await identity.AuthenticateAsync("mfa-actor@example.uz", "correct horse battery staple"))!;
                var targetEnrollment = await identity.EnrollOperatorMfaAsync(targetId, "correct horse battery staple");
                targetSecret = targetEnrollment.SharedSecret;
                var actorEnrollment = await identity.EnrollOperatorMfaAsync(actorId, "correct horse battery staple");
                var totp = new TotpAuthenticator();
                var targetCode = totp.CreateCode(targetSecret, clock.GetUtcNow());
                Assert.False(await identity.VerifyOperatorMfaAsync(targetSession.SessionToken, targetCode));
                Assert.True(await identity.ConfirmOperatorMfaAsync(targetSession.SessionToken, targetCode));
                Assert.False(await identity.ConfirmOperatorMfaAsync(targetSecondSession.SessionToken, targetCode));
                Assert.False(await identity.VerifyOperatorMfaAsync(targetSecondSession.SessionToken, targetCode));
                Assert.True(await identity.ConfirmOperatorMfaAsync(actorSession.SessionToken,
                    totp.CreateCode(actorEnrollment.SharedSecret, clock.GetUtcNow())));
                Assert.True(await identity.HasRecentOperatorReauthenticationAsync(targetSession.SessionToken));
            }

            clock.Advance(TimeSpan.FromSeconds(30));
            var nextCode = new TotpAuthenticator().CreateCode(targetSecret, clock.GetUtcNow());
            await using var firstScope = services.CreateAsyncScope();
            await using var secondScope = services.CreateAsyncScope();
            var results = await Task.WhenAll(
                firstScope.ServiceProvider.GetRequiredService<IIdentityService>()
                    .VerifyOperatorMfaAsync(targetSession.SessionToken, nextCode),
                secondScope.ServiceProvider.GetRequiredService<IIdentityService>()
                    .VerifyOperatorMfaAsync(targetSecondSession.SessionToken, nextCode));
            Assert.Single(results, accepted => accepted);

            await using (var recoveryScope = services.CreateAsyncScope())
            {
                var identity = recoveryScope.ServiceProvider.GetRequiredService<IIdentityService>();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => identity.ResetOperatorMfaAsync(
                    targetSession.SessionToken, targetId, "verified recovery INC-28"));
                Assert.True(await identity.ResetOperatorMfaAsync(actorSession.SessionToken, targetId,
                    "verified recovery INC-28"));
                Assert.False(await identity.ResetOperatorMfaAsync(actorSession.SessionToken, targetId,
                    "verified recovery INC-28"));
                Assert.Null(await identity.AuthenticateSessionAsync(targetSession.SessionToken));
                Assert.Null(await identity.AuthenticateSessionAsync(targetSecondSession.SessionToken));
            }

            await using var checkScope = services.CreateAsyncScope();
            var db = checkScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var access = await db.Set<IdentityOperatorAccessEntity>().AsNoTracking()
                .SingleAsync(row => row.AccountId == targetId);
            Assert.Null(access.ProtectedTotpSecret);
            Assert.Null(access.MfaEnabledAt);
            Assert.Null(access.MfaEnrollmentExpiresAt);
            Assert.Null(access.LastTotpStep);
            var events = await db.Set<AuditEventEntity>().AsNoTracking()
                .Where(row => row.ResourceType == "operator")
                .ToListAsync();
            Assert.Equal(2, events.Count(row => row.Action == "operator.mfa.enrollment_started"));
            Assert.Equal(2, events.Count(row => row.Action == "operator.mfa.enabled"));
            var reset = Assert.Single(events, row => row.Action == "operator.mfa.reset");
            Assert.Equal(actorId, reset.ActorAccountId);
            Assert.Equal(targetId, reset.ResourceId);
            Assert.Contains("verified recovery INC-28", reset.MetadataJson);
            Assert.DoesNotContain(targetSecret, reset.MetadataJson);
        }
        finally
        {
            await using var cleanupProvider = fixture.CreateServiceProvider();
            await using var cleanupScope = cleanupProvider.CreateAsyncScope();
            await cleanupScope.ServiceProvider.GetRequiredService<FoundationDbContext>()
                .Database.ExecuteSqlRawAsync("TRUNCATE TABLE audit.audit_event");
        }
    }

    private sealed class MfaClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset current = initial;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
