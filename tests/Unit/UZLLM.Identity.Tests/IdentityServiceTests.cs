using UZLLM.Modules.Identity.Application;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;

namespace UZLLM.Identity.Tests;

public sealed class IdentityServiceTests
{
    [Fact]
    public async Task AuthenticateAsync_performs_password_work_for_missing_and_unverified_accounts()
    {
        var fixture = new IdentityFixture();
        var hasher = new CountingPasswordHasher();
        var service = new IdentityService(fixture.Store, hasher, new TestSecretProtector(),
            fixture.Totp, fixture.Clock, fixture.Notifications, new IdentityNoopTransactionCoordinator(), fixture.Audit);

        Assert.Null(await service.AuthenticateAsync("missing@example.uz", "candidate-password"));
        var registration = await service.RegisterAsync("pending@example.uz", "candidate-password");
        Assert.Null(await service.AuthenticateAsync("pending@example.uz", "candidate-password"));
        Assert.Equal(2, hasher.UnknownCalls);
        Assert.Equal(0, hasher.KnownCalls);

        Assert.True(await service.VerifyEmailAsync(registration.VerificationToken));
        Assert.Null(await service.AuthenticateAsync("pending@example.uz", "candidate-password"));
        Assert.Equal(1, hasher.KnownCalls);
        Assert.Equal(2, hasher.UnknownCalls);
    }

    [Fact]
    public void Verify_with_a_malformed_persisted_password_hash_fails_closed()
    {
        var passwordHasher = new Pbkdf2PasswordHasher();

        Assert.False(passwordHasher.Verify("correct horse battery staple", "uzllm-pbkdf2-sha512$600000$not-base64$not-base64"));
    }

    [Fact]
    public async Task RegisterAsync_stores_a_non_plaintext_password_and_verifies_the_account_once()
    {
        var fixture = new IdentityFixture();

        var registration = await fixture.Service.RegisterAsync("person@example.uz", "correct horse battery staple");

        var account = await fixture.Store.GetAccountAsync(registration.AccountId);
        Assert.NotNull(account);
        Assert.NotEqual("correct horse battery staple", account.PasswordHash);
        Assert.False(account.IsEmailVerified);
        var notification = Assert.Single(fixture.Notifications.Sent);
        Assert.Equal(IdentityEmailKind.Verification, notification.Kind);
        Assert.Equal(registration.VerificationToken, notification.Token);
        Assert.Equal("person@example.uz", notification.Email);

        Assert.True(await fixture.Service.VerifyEmailAsync(registration.VerificationToken));
        Assert.False(await fixture.Service.VerifyEmailAsync(registration.VerificationToken));
        Assert.True((await fixture.Store.GetAccountAsync(registration.AccountId))!.IsEmailVerified);
    }

    [Fact]
    public async Task AuthenticateAsync_issues_an_opaque_server_backed_session_only_for_verified_credentials()
    {
        var fixture = new IdentityFixture();
        var registration = await fixture.Service.RegisterAsync("person@example.uz", "correct horse battery staple");

        Assert.Null(await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple"));

        Assert.True(await fixture.Service.VerifyEmailAsync(registration.VerificationToken));

        var session = await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple");

        Assert.NotNull(session);
        Assert.DoesNotContain("person@example.uz", session.SessionToken, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correct horse battery staple", session.SessionToken, StringComparison.Ordinal);
        Assert.Equal(registration.AccountId, (await fixture.Service.AuthenticateSessionAsync(session.SessionToken))!.AccountId);
    }

    [Fact]
    public async Task AuthenticateSessionAsync_rejects_expired_or_revoked_sessions()
    {
        var fixture = new IdentityFixture();
        await fixture.RegisterAndVerifyAsync();
        var session = await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple");
        Assert.NotNull(session);

        fixture.Clock.Advance(TimeSpan.FromDays(15));
        Assert.Null(await fixture.Service.AuthenticateSessionAsync(session.SessionToken));

        var activeSession = await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple");
        Assert.NotNull(activeSession);
        await fixture.Service.RevokeSessionAsync(activeSession.SessionToken);
        Assert.Null(await fixture.Service.AuthenticateSessionAsync(activeSession.SessionToken));
    }

    [Fact]
    public async Task ValidateAsync_requires_a_matching_csrf_cookie_and_header_for_an_authenticated_session()
    {
        var fixture = new IdentityFixture();
        var registration = await fixture.RegisterAndVerifyAsync();
        var session = await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple");
        Assert.NotNull(session);

        Assert.True(await fixture.Csrf.ValidateAsync(session.SessionToken, session.CsrfToken, session.CsrfToken));
        Assert.False(await fixture.Csrf.ValidateAsync(session.SessionToken, session.CsrfToken, "other-token"));
        Assert.False(await fixture.Csrf.ValidateAsync(session.SessionToken, null, session.CsrfToken));
        Assert.False(await fixture.Csrf.ValidateAsync($"{registration.AccountId:N}.unrelated-session-token", session.CsrfToken, session.CsrfToken));
    }

    [Fact]
    public async Task ResetPasswordAsync_consumes_the_recovery_challenge_and_revokes_existing_sessions()
    {
        var fixture = new IdentityFixture();
        await fixture.RegisterAndVerifyAsync();
        var session = await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple");
        Assert.NotNull(session);

        var recoveryToken = await fixture.Service.BeginPasswordRecoveryAsync("person@example.uz");
        Assert.NotNull(recoveryToken);
        var recoveryEmail = Assert.Single(fixture.Notifications.Sent,
            value => value.Kind == IdentityEmailKind.PasswordRecovery);
        Assert.Equal(recoveryToken, recoveryEmail.Token);
        Assert.True(await fixture.Service.ResetPasswordAsync(recoveryToken, "another correct battery staple"));
        Assert.False(await fixture.Service.ResetPasswordAsync(recoveryToken, "third correct battery staple"));
        Assert.Null(await fixture.Service.AuthenticateSessionAsync(session.SessionToken));
        Assert.Null(await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple"));
        Assert.NotNull(await fixture.Service.AuthenticateAsync("person@example.uz", "another correct battery staple"));
    }

    [Fact]
    public async Task ResetPasswordAsync_rejects_an_expired_recovery_challenge()
    {
        var fixture = new IdentityFixture();
        await fixture.RegisterAndVerifyAsync();
        var recoveryToken = await fixture.Service.BeginPasswordRecoveryAsync("person@example.uz");
        Assert.NotNull(recoveryToken);

        fixture.Clock.Advance(TimeSpan.FromMinutes(31));

        Assert.False(await fixture.Service.ResetPasswordAsync(recoveryToken, "another correct battery staple"));
        Assert.NotNull(await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple"));
    }

    [Fact]
    public async Task VerifyOperatorMfaAsync_requires_a_valid_totp_and_records_recent_reauthentication()
    {
        var fixture = new IdentityFixture();
        var registration = await fixture.RegisterAndVerifyAsync();
        var session = await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple");
        Assert.NotNull(session);
        Assert.False(await fixture.Service.HasRecentOperatorReauthenticationAsync(session.SessionToken));
        await fixture.Service.GrantOperatorAccessAsync(registration.AccountId);
        var enrollment = await fixture.Service.EnrollOperatorMfaAsync(registration.AccountId,
            "correct horse battery staple");

        Assert.False(await fixture.Service.VerifyOperatorMfaAsync(session.SessionToken, "000000"));
        var confirmationCode = fixture.Totp.CreateCode(enrollment.SharedSecret, fixture.Clock.GetUtcNow());
        Assert.False(await fixture.Service.VerifyOperatorMfaAsync(session.SessionToken, confirmationCode));
        Assert.True(await fixture.Service.ConfirmOperatorMfaAsync(session.SessionToken, confirmationCode));
        Assert.False(await fixture.Service.ConfirmOperatorMfaAsync(session.SessionToken, confirmationCode));
        Assert.False(await fixture.Service.VerifyOperatorMfaAsync(session.SessionToken, confirmationCode));
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(await fixture.Service.VerifyOperatorMfaAsync(session.SessionToken,
            fixture.Totp.CreateCode(enrollment.SharedSecret, fixture.Clock.GetUtcNow())));
        Assert.True(await fixture.Service.HasRecentOperatorReauthenticationAsync(session.SessionToken));

        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.False(await fixture.Service.HasRecentOperatorReauthenticationAsync(session.SessionToken));
    }

    [Fact]
    public async Task Operator_Mfa_enrollment_requires_password_and_cannot_replace_an_existing_secret()
    {
        var fixture = new IdentityFixture();
        var registration = await fixture.RegisterAndVerifyAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.EnrollOperatorMfaAsync(
            registration.AccountId, "correct horse battery staple"));
        await fixture.Service.GrantOperatorAccessAsync(registration.AccountId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.EnrollOperatorMfaAsync(
            registration.AccountId, "wrong password"));
        var enrollment = await fixture.Service.EnrollOperatorMfaAsync(registration.AccountId,
            "correct horse battery staple");
        Assert.False(await fixture.Service.HasRecentOperatorReauthenticationAsync(
            (await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple"))!.SessionToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.EnrollOperatorMfaAsync(
            registration.AccountId, "correct horse battery staple"));
        Assert.Single(fixture.Audit.Events, entry => entry.Action == "operator.mfa.enrollment_started");
        Assert.NotNull(enrollment.SharedSecret);
    }

    [Fact]
    public async Task Pending_Mfa_requires_a_current_code_before_expiry_and_can_restart_after_expiry()
    {
        var fixture = new IdentityFixture();
        var registration = await fixture.RegisterAndVerifyAsync();
        await fixture.Service.GrantOperatorAccessAsync(registration.AccountId);
        var session = (await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple"))!;
        var first = await fixture.Service.EnrollOperatorMfaAsync(registration.AccountId,
            "correct horse battery staple");
        Assert.Equal(fixture.Clock.GetUtcNow().AddMinutes(10), first.ExpiresAt);
        Assert.False(await fixture.Service.ConfirmOperatorMfaAsync(session.SessionToken, "000000"));
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(await fixture.Service.ConfirmOperatorMfaAsync(session.SessionToken,
            fixture.Totp.CreateCode(first.SharedSecret, fixture.Clock.GetUtcNow())));
        var replacement = await fixture.Service.EnrollOperatorMfaAsync(registration.AccountId,
            "correct horse battery staple");
        Assert.NotEqual(first.SharedSecret, replacement.SharedSecret);
        Assert.False(await fixture.Service.ConfirmOperatorMfaAsync(session.SessionToken,
            fixture.Totp.CreateCode(first.SharedSecret, fixture.Clock.GetUtcNow())));
        Assert.True(await fixture.Service.ConfirmOperatorMfaAsync(session.SessionToken,
            fixture.Totp.CreateCode(replacement.SharedSecret, fixture.Clock.GetUtcNow())));
        Assert.Equal(2, fixture.Audit.Events.Count(entry => entry.Action == "operator.mfa.enrollment_started"));
        Assert.Single(fixture.Audit.Events, entry => entry.Action == "operator.mfa.enabled");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.EnrollOperatorMfaAsync(
            registration.AccountId, "correct horse battery staple"));
    }

    [Fact]
    public async Task Operator_Mfa_code_is_single_use_across_sessions_even_when_submitted_concurrently()
    {
        var fixture = new IdentityFixture();
        var registration = await fixture.RegisterAndVerifyAsync();
        await fixture.Service.GrantOperatorAccessAsync(registration.AccountId);
        var firstSession = (await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple"))!;
        var secondSession = (await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple"))!;
        var enrollment = await fixture.Service.EnrollOperatorMfaAsync(registration.AccountId,
            "correct horse battery staple");
        Assert.True(await fixture.Service.ConfirmOperatorMfaAsync(firstSession.SessionToken,
            fixture.Totp.CreateCode(enrollment.SharedSecret, fixture.Clock.GetUtcNow())));
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        var code = fixture.Totp.CreateCode(enrollment.SharedSecret, fixture.Clock.GetUtcNow());
        var results = await Task.WhenAll(
            fixture.Service.VerifyOperatorMfaAsync(firstSession.SessionToken, code),
            fixture.Service.VerifyOperatorMfaAsync(secondSession.SessionToken, code));
        Assert.Single(results, accepted => accepted);
        Assert.False(await fixture.Service.VerifyOperatorMfaAsync(firstSession.SessionToken, code));
        Assert.False(await fixture.Service.VerifyOperatorMfaAsync(secondSession.SessionToken, code));
    }

    [Fact]
    public async Task Operator_Mfa_recovery_requires_another_recent_operator_and_revokes_target_sessions()
    {
        var fixture = new IdentityFixture();
        var target = await fixture.RegisterAndVerifyAsync();
        var actor = await fixture.Service.RegisterAsync("rescuer@example.uz", "correct horse battery staple");
        Assert.True(await fixture.Service.VerifyEmailAsync(actor.VerificationToken));
        await fixture.Service.GrantOperatorAccessAsync(target.AccountId);
        await fixture.Service.GrantOperatorAccessAsync(actor.AccountId);
        var targetSession = (await fixture.Service.AuthenticateAsync("person@example.uz", "correct horse battery staple"))!;
        var actorSession = (await fixture.Service.AuthenticateAsync("rescuer@example.uz", "correct horse battery staple"))!;
        var targetEnrollment = await fixture.Service.EnrollOperatorMfaAsync(target.AccountId,
            "correct horse battery staple");
        var actorEnrollment = await fixture.Service.EnrollOperatorMfaAsync(actor.AccountId,
            "correct horse battery staple");
        Assert.True(await fixture.Service.ConfirmOperatorMfaAsync(targetSession.SessionToken,
            fixture.Totp.CreateCode(targetEnrollment.SharedSecret, fixture.Clock.GetUtcNow())));
        Assert.True(await fixture.Service.ConfirmOperatorMfaAsync(actorSession.SessionToken,
            fixture.Totp.CreateCode(actorEnrollment.SharedSecret, fixture.Clock.GetUtcNow())));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ResetOperatorMfaAsync(
            targetSession.SessionToken, target.AccountId, "identity incident INC-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ResetOperatorMfaAsync(
            actorSession.SessionToken, target.AccountId, "short"));
        Assert.True(await fixture.Service.ResetOperatorMfaAsync(actorSession.SessionToken,
            target.AccountId, "identity incident INC-1"));
        Assert.False(await fixture.Service.ResetOperatorMfaAsync(actorSession.SessionToken,
            target.AccountId, "identity incident INC-1"));
        Assert.Null(await fixture.Service.AuthenticateSessionAsync(targetSession.SessionToken));
        var reset = Assert.Single(fixture.Audit.Events, entry => entry.Action == "operator.mfa.reset");
        Assert.Equal(actor.AccountId, reset.ActorAccountId);
        Assert.Equal(target.AccountId, reset.ResourceId);
        Assert.Contains("identity incident INC-1", reset.MetadataJson);
    }
}

internal sealed class CountingPasswordHasher : IPasswordHasher
{
    public int KnownCalls { get; private set; }
    public int UnknownCalls { get; private set; }
    public string Hash(string password) => "test-password-hash";
    public bool Verify(string password, string passwordHash)
    {
        KnownCalls++;
        return false;
    }
    public bool VerifyUnknown(string password)
    {
        UnknownCalls++;
        return false;
    }
}
