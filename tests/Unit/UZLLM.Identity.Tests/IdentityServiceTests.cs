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
            fixture.Totp, fixture.Clock, fixture.Notifications, new IdentityNoopTransactionCoordinator());

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
        var enrollment = await fixture.Service.EnrollOperatorMfaAsync(registration.AccountId);

        Assert.False(await fixture.Service.VerifyOperatorMfaAsync(session.SessionToken, "000000"));
        Assert.True(await fixture.Service.VerifyOperatorMfaAsync(session.SessionToken, fixture.Totp.CreateCode(enrollment.SharedSecret, fixture.Clock.GetUtcNow())));
        Assert.True(await fixture.Service.HasRecentOperatorReauthenticationAsync(session.SessionToken));

        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.False(await fixture.Service.HasRecentOperatorReauthenticationAsync(session.SessionToken));
    }

    [Fact]
    public async Task Operator_Mfa_enrollment_requires_password_and_cannot_replace_an_existing_secret()
    {
        var fixture = new IdentityFixture();
        var registration = await fixture.RegisterAndVerifyAsync();
        Assert.False(await fixture.Service.VerifyOperatorPasswordAsync(registration.AccountId, "correct horse battery staple"));
        await fixture.Service.GrantOperatorAccessAsync(registration.AccountId);
        Assert.False(await fixture.Service.VerifyOperatorPasswordAsync(registration.AccountId, "wrong password"));
        Assert.True(await fixture.Service.VerifyOperatorPasswordAsync(registration.AccountId, "correct horse battery staple"));
        _ = await fixture.Service.EnrollOperatorMfaAsync(registration.AccountId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.EnrollOperatorMfaAsync(registration.AccountId));
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
