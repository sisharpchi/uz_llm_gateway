using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Identity.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Notifications.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Modules.Projects.Infrastructure;
using UZLLM.Persistence;

namespace UZLLM.Persistence.IntegrationTests;

[Collection(nameof(PersistenceIntegrationCollection))]
public sealed class TeamIntegrationTests(PersistenceIntegrationFixture fixture)
{
    [Fact]
    public async Task Invitation_is_encrypted_email_bound_and_accepts_only_once_under_replay()
    {
        await ResetAsync();
        await using var provider = CreateProvider();
        await using var seed = provider.CreateAsyncScope();
        var owner = await RegisterAsync(seed, "team-owner@example.uz");
        var invited = await RegisterAsync(seed, "team-invited@example.uz");
        var wrong = await RegisterAsync(seed, "team-wrong@example.uz");
        var org = await seed.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "Team tenant");
        var team = seed.ServiceProvider.GetRequiredService<ITeamService>();
        var invitation = await team.InviteAsync(owner, org.Id, " TEAM-INVITED@example.uz ",
            OrganizationMemberRole.Developer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => team.InviteAsync(owner, org.Id,
            "team-invited@example.uz", OrganizationMemberRole.Developer));
        var message = (await seed.ServiceProvider.GetRequiredService<IOutboxStore>()
            .ClaimAvailableAsync("team-test", 20, TimeSpan.FromMinutes(1)))
            .Single(value => value.EventType == IdentityEmailEventTypes.TeamInvitation);
        var notification = seed.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>()
            .Unprotect(message.Payload);
        Assert.Equal("team-invited@example.uz", invitation.Email);
        Assert.Equal(invitation.Email, notification.Email);
        Assert.DoesNotContain(notification.Token, message.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(invitation.Email, message.Payload, StringComparison.Ordinal);
        var sender = new RecordingTeamSender();
        var handler = new IdentityEmailOutboxHandler(IdentityEmailEventTypes.TeamInvitation,
            seed.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>(), sender,
            seed.ServiceProvider.GetRequiredService<IConsumerInboxStore>(), TimeProvider.System);
        await handler.HandleAsync(message, default);
        await handler.HandleAsync(message, default);
        Assert.Equal(notification.Token, Assert.Single(sender.Sent).Token);
        var db = seed.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var row = await db.Set<OrganizationInvitationEntity>().SingleAsync(value => value.Id == invitation.Id);
        Assert.Equal(32, row.TokenHash.Length);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(notification.Token)), row.TokenHash);
        Assert.Null(await team.AcceptAsync(wrong, notification.Token));

        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var results = await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<ITeamService>()
                .AcceptAsync(invited, notification.Token),
            secondScope.ServiceProvider.GetRequiredService<ITeamService>()
                .AcceptAsync(invited, notification.Token));
        Assert.Single(results, result => result is not null);
        Assert.Single(results, result => result is null);
        Assert.Null(await team.AcceptAsync(invited, notification.Token));
        var member = await team.ListMembersAsync(owner, org.Id);
        Assert.Equal(2, member.Count);
        Assert.Equal(OrganizationMemberRole.Developer,
            member.Single(value => value.AccountId == invited).Role);
    }

    [Fact]
    public async Task Grant_role_and_revoke_change_existing_session_authorization_immediately()
    {
        await ResetAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "access-owner@example.uz");
        var developer = await RegisterAsync(scope, "access-developer@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "Access tenant");
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var first = await projects.CreateAsync(owner, org.Id, "First");
        var second = await projects.CreateAsync(owner, org.Id, "Second");
        var team = scope.ServiceProvider.GetRequiredService<ITeamService>();
        var token = await InviteTokenAsync(scope, owner, org.Id,
            "access-developer@example.uz", OrganizationMemberRole.Developer);
        Assert.NotNull(await team.AcceptAsync(developer, token));
        var authorization = scope.ServiceProvider.GetRequiredService<IOrganizationAuthorizationService>();
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsurePermissionAsync(developer, org.Id,
                OrganizationPermission.ManageApiKeys, first.Id));
        Assert.True(await team.SetProjectGrantAsync(owner, org.Id, developer, first.Id, true));
        Assert.True(await team.SetProjectGrantAsync(owner, org.Id, developer, first.Id, true));
        await authorization.EnsurePermissionAsync(developer, org.Id,
            OrganizationPermission.ManageApiKeys, first.Id);
        Assert.Equal(first.Id, (await scope.ServiceProvider.GetRequiredService<IProjectAccessService>()
            .GetAuthorizedAsync(developer, first.Id, OrganizationPermission.ManageApiKeys))?.Id);
        Assert.Equal(first.Id, Assert.Single(await projects.ListAsync(developer, org.Id)).Id);
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsurePermissionAsync(developer, org.Id,
                OrganizationPermission.ReadProjects, second.Id));
        Assert.True(await team.ChangeRoleAsync(owner, org.Id, developer,
            OrganizationMemberRole.ReadOnly));
        Assert.True(await team.ChangeRoleAsync(owner, org.Id, developer,
            OrganizationMemberRole.ReadOnly));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsurePermissionAsync(developer, org.Id,
                OrganizationPermission.ManageApiKeys, first.Id));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            scope.ServiceProvider.GetRequiredService<IProjectAccessService>()
                .GetAuthorizedAsync(developer, first.Id, OrganizationPermission.ManageApiKeys));
        Assert.True(await team.SetProjectGrantAsync(owner, org.Id, developer, first.Id, false));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsurePermissionAsync(developer, org.Id,
                OrganizationPermission.ReadProjects, first.Id));
        Assert.True(await team.RevokeAsync(owner, org.Id, developer));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            projects.ListAsync(developer, org.Id));
    }

    [Fact]
    public async Task Admin_cannot_escalate_or_remove_owner_and_last_owner_is_preserved()
    {
        await ResetAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "privilege-owner@example.uz");
        var admin = await RegisterAsync(scope, "privilege-admin@example.uz");
        var developer = await RegisterAsync(scope, "privilege-dev@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "Privilege tenant");
        var team = scope.ServiceProvider.GetRequiredService<ITeamService>();
        var adminToken = await InviteTokenAsync(scope, owner, org.Id,
            "privilege-admin@example.uz", OrganizationMemberRole.Admin);
        Assert.NotNull(await team.AcceptAsync(admin, adminToken));
        var devToken = await InviteTokenAsync(scope, admin, org.Id,
            "privilege-dev@example.uz", OrganizationMemberRole.Developer);
        Assert.NotNull(await team.AcceptAsync(developer, devToken));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            team.InviteAsync(admin, org.Id, "new-admin@example.uz", OrganizationMemberRole.Admin));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            team.ChangeRoleAsync(admin, org.Id, developer, OrganizationMemberRole.Owner));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            team.RevokeAsync(admin, org.Id, owner));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            team.RevokeAsync(owner, org.Id, owner));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            team.ChangeRoleAsync(owner, org.Id, owner, OrganizationMemberRole.Developer));
        var authorization = scope.ServiceProvider.GetRequiredService<IOrganizationAuthorizationService>();
        await authorization.EnsureOwnerAsync(owner, org.Id);
        Assert.True(await team.ChangeRoleAsync(owner, org.Id, developer,
            OrganizationMemberRole.Owner));
        Assert.True(await team.RevokeAsync(owner, org.Id, owner));
        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.EnsureOwnerAsync(owner, org.Id));
        await authorization.EnsureOwnerAsync(developer, org.Id);
    }

    [Fact]
    public async Task Expired_invite_can_be_replaced_and_cross_tenant_project_grant_is_rejected()
    {
        await ResetAsync();
        var clock = new AdjustableTeamClock(DateTimeOffset.UtcNow);
        await using var provider = CreateProvider(clock);
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "expiry-owner@example.uz");
        var invited = await RegisterAsync(scope, "expiry-member@example.uz");
        var orgs = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var firstOrg = await orgs.CreateAsync(owner, "Expiry tenant");
        var secondOrg = await orgs.CreateAsync(owner, "Other tenant");
        var otherProject = await scope.ServiceProvider.GetRequiredService<IProjectService>()
            .CreateAsync(owner, secondOrg.Id, "Other project");
        var oldToken = await InviteTokenAsync(scope, owner, firstOrg.Id,
            "expiry-member@example.uz", OrganizationMemberRole.Developer);
        clock.Advance(TimeSpan.FromDays(8));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ITeamService>()
            .AcceptAsync(invited, oldToken));
        var freshToken = await InviteTokenAsync(scope, owner, firstOrg.Id,
            "expiry-member@example.uz", OrganizationMemberRole.Developer);
        Assert.NotEqual(oldToken, freshToken);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ITeamService>()
            .AcceptAsync(invited, oldToken));
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ITeamService>()
            .AcceptAsync(invited, freshToken));
        Assert.False(await scope.ServiceProvider.GetRequiredService<ITeamService>()
            .SetProjectGrantAsync(owner, firstOrg.Id, invited, otherProject.Id, true));
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.Set<ProjectGrantEntity>().Add(new ProjectGrantEntity
        {
            OrganizationId = firstOrg.Id, AccountId = invited, ProjectId = otherProject.Id,
            CreatedAt = clock.GetUtcNow()
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Notification_failure_rolls_back_invitation_and_audit()
    {
        await ResetAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "rollback-team-owner@example.uz");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "Rollback team tenant");
        var services = scope.ServiceProvider;
        var failing = new UZLLM.Modules.Organizations.Application.TeamService(
            services.GetRequiredService<ITeamStore>(),
            services.GetRequiredService<IOrganizationStore>(),
            services.GetRequiredService<IIdentityStore>(),
            new FailingTeamEmailQueue(),
            services.GetRequiredService<UZLLM.Modules.Audit.Contracts.IAuditTrail>(),
            services.GetRequiredService<ITransactionCoordinator>(), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.InviteAsync(owner, org.Id,
            "rollback-team-member@example.uz", OrganizationMemberRole.Developer));
        Assert.Empty(await services.GetRequiredService<ITeamService>()
            .ListInvitationsAsync(owner, org.Id));
        Assert.Empty(await services.GetRequiredService<FoundationDbContext>()
            .Set<AuditEventEntity>().AsNoTracking()
            .Where(value => value.OrganizationId == org.Id && value.Action == "team.invited")
            .ToListAsync());
    }

    [Fact]
    public async Task Unverified_matching_account_cannot_accept_until_email_is_verified()
    {
        await ResetAsync();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var owner = await RegisterAsync(scope, "verify-invite-owner@example.uz");
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var invited = await identity.RegisterAsync("verify-invite-member@example.uz",
            "correct horse battery staple");
        var org = await scope.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(owner, "Verify invite tenant");
        var token = await InviteTokenAsync(scope, owner, org.Id,
            "verify-invite-member@example.uz", OrganizationMemberRole.ReadOnly);
        var team = scope.ServiceProvider.GetRequiredService<ITeamService>();
        Assert.Null(await team.AcceptAsync(invited.AccountId, token));
        Assert.True(await identity.VerifyEmailAsync(invited.VerificationToken));
        Assert.Equal(OrganizationMemberRole.ReadOnly,
            (await team.AcceptAsync(invited.AccountId, token))?.Role);
    }

    [Fact]
    public async Task Concurrent_owner_revocations_leave_one_active_owner()
    {
        await ResetAsync();
        await using var provider = CreateProvider();
        await using var seed = provider.CreateAsyncScope();
        var first = await RegisterAsync(seed, "concurrent-owner-1@example.uz");
        var second = await RegisterAsync(seed, "concurrent-owner-2@example.uz");
        var org = await seed.ServiceProvider.GetRequiredService<IOrganizationService>()
            .CreateAsync(first, "Concurrent owner tenant");
        var token = await InviteTokenAsync(seed, first, org.Id,
            "concurrent-owner-2@example.uz", OrganizationMemberRole.Developer);
        var team = seed.ServiceProvider.GetRequiredService<ITeamService>();
        Assert.NotNull(await team.AcceptAsync(second, token));
        Assert.True(await team.ChangeRoleAsync(first, org.Id, second,
            OrganizationMemberRole.Owner));
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var outcomes = await Task.WhenAll(
            TryRevokeAsync(firstScope.ServiceProvider.GetRequiredService<ITeamService>(),
                first, org.Id, second),
            TryRevokeAsync(secondScope.ServiceProvider.GetRequiredService<ITeamService>(),
                second, org.Id, first));
        Assert.Single(outcomes, value => value);
        var activeOwners = (await team.ListMembersAsync(outcomes[0] ? first : second, org.Id))
            .Count(member => member.Role == OrganizationMemberRole.Owner
                && member.Status == OrganizationMemberStatus.Active);
        Assert.Equal(1, activeOwners);
    }

    private async Task ResetAsync()
    {
        await fixture.ResetMigrationsAsync();
        await fixture.ApplyMigrationsAsync();
    }

    private ServiceProvider CreateProvider(TimeProvider? clock = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Postgres"] = fixture.RuntimeConnectionString }).Build();
        var services = new ServiceCollection();
        if (clock is not null) services.AddSingleton(clock);
        return services.AddUzllmPersistence(config).AddUzllmIdentity().AddUzllmAudit()
            .AddUzllmOrganizations().AddUzllmTeam().AddUzllmProjects()
            .BuildServiceProvider(validateScopes: true);
    }

    private static async Task<Guid> RegisterAsync(AsyncServiceScope scope, string email)
    {
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var registration = await identity.RegisterAsync(email, "correct horse battery staple");
        Assert.True(await identity.VerifyEmailAsync(registration.VerificationToken));
        return registration.AccountId;
    }

    private static async Task<string> InviteTokenAsync(AsyncServiceScope scope, Guid actorId,
        Guid organizationId, string email, OrganizationMemberRole role)
    {
        var invitation = await scope.ServiceProvider.GetRequiredService<ITeamService>()
            .InviteAsync(actorId, organizationId, email, role);
        var messages = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .ClaimAvailableAsync("team-test", 50, TimeSpan.FromMinutes(1));
        var message = messages.Where(value => value.EventType == IdentityEmailEventTypes.TeamInvitation
            && scope.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>()
                .Unprotect(value.Payload).Email == invitation.Email)
            .OrderByDescending(value => value.OccurredAt).First();
        return scope.ServiceProvider.GetRequiredService<IdentityEmailPayloadCodec>()
            .Unprotect(message.Payload).Token;
    }

    private sealed class AdjustableTeamClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }

    private sealed class FailingTeamEmailQueue : IIdentityNotificationQueue
    {
        public Task QueueAsync(IdentityEmailNotification notification,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated email queue failure.");
    }

    private sealed class RecordingTeamSender : IIdentityEmailSender
    {
        public List<IdentityEmailNotification> Sent { get; } = [];
        public Task SendAsync(Guid messageId, IdentityEmailNotification notification,
            CancellationToken cancellationToken = default)
        {
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private static async Task<bool> TryRevokeAsync(ITeamService team, Guid actorId,
        Guid organizationId, Guid accountId)
    {
        try { return await team.RevokeAsync(actorId, organizationId, accountId); }
        catch (TenantAccessDeniedException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
