using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed class FoundationDbContext(DbContextOptions<FoundationDbContext> options) : DbContext(options)
{
    internal DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    internal DbSet<ConsumerInboxEntryEntity> ConsumerInboxEntries => Set<ConsumerInboxEntryEntity>();

    internal DbSet<LeasedJobEntity> LeasedJobs => Set<LeasedJobEntity>();

    internal DbSet<OperationalAlertEntity> OperationalAlerts => Set<OperationalAlertEntity>();

    internal DbSet<IdentityAccountEntity> IdentityAccounts => Set<IdentityAccountEntity>();

    internal DbSet<IdentitySessionEntity> IdentitySessions => Set<IdentitySessionEntity>();

    internal DbSet<IdentityChallengeEntity> IdentityChallenges => Set<IdentityChallengeEntity>();

    internal DbSet<IdentityOperatorAccessEntity> IdentityOperatorAccesses => Set<IdentityOperatorAccessEntity>();

    internal DbSet<OrganizationEntity> Organizations => Set<OrganizationEntity>();

    internal DbSet<OrganizationMemberEntity> OrganizationMembers => Set<OrganizationMemberEntity>();

    internal DbSet<ProjectEntity> Projects => Set<ProjectEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityAccountEntity>(entity =>
        {
            entity.ToTable("user", "iam");
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Id).HasColumnName("id");
            entity.Property(account => account.Email).HasColumnName("email").HasMaxLength(320);
            entity.Property(account => account.PasswordHash).HasColumnName("password_hash");
            entity.Property(account => account.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(account => account.EmailVerifiedAt).HasColumnName("email_verified_at");
            entity.Property(account => account.CreatedAt).HasColumnName("created_at");
            entity.Property(account => account.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(account => account.Email).IsUnique();
        });

        modelBuilder.Entity<IdentitySessionEntity>(entity =>
        {
            entity.ToTable("session", "iam");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).HasColumnName("id");
            entity.Property(session => session.AccountId).HasColumnName("account_id");
            entity.Property(session => session.SecretHash).HasColumnName("secret_hash");
            entity.Property(session => session.CsrfHash).HasColumnName("csrf_hash");
            entity.Property(session => session.CreatedAt).HasColumnName("created_at");
            entity.Property(session => session.ExpiresAt).HasColumnName("expires_at");
            entity.Property(session => session.RevokedAt).HasColumnName("revoked_at");
            entity.Property(session => session.MfaReauthenticatedAt).HasColumnName("mfa_reauthenticated_at");
            entity.HasIndex(session => new { session.AccountId, session.ExpiresAt });
            entity.HasOne(session => session.Account)
                .WithMany(account => account.Sessions)
                .HasForeignKey(session => session.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdentityChallengeEntity>(entity =>
        {
            entity.ToTable("challenge", "iam");
            entity.HasKey(challenge => challenge.Id);
            entity.Property(challenge => challenge.Id).HasColumnName("id");
            entity.Property(challenge => challenge.AccountId).HasColumnName("account_id");
            entity.Property(challenge => challenge.Kind).HasColumnName("kind").HasMaxLength(50);
            entity.Property(challenge => challenge.TokenHash).HasColumnName("token_hash");
            entity.Property(challenge => challenge.CreatedAt).HasColumnName("created_at");
            entity.Property(challenge => challenge.ExpiresAt).HasColumnName("expires_at");
            entity.Property(challenge => challenge.ConsumedAt).HasColumnName("consumed_at");
            entity.HasIndex(challenge => new { challenge.Kind, challenge.TokenHash }).IsUnique();
            entity.HasIndex(challenge => new { challenge.AccountId, challenge.Kind })
                .HasFilter("consumed_at IS NULL")
                .IsUnique();
            entity.HasOne(challenge => challenge.Account)
                .WithMany(account => account.Challenges)
                .HasForeignKey(challenge => challenge.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdentityOperatorAccessEntity>(entity =>
        {
            entity.ToTable("operator_access", "iam");
            entity.HasKey(access => access.AccountId);
            entity.Property(access => access.AccountId).HasColumnName("account_id");
            entity.Property(access => access.IsActive).HasColumnName("is_active");
            entity.Property(access => access.ProtectedTotpSecret).HasColumnName("protected_totp_secret");
            entity.Property(access => access.MfaEnabledAt).HasColumnName("mfa_enabled_at");
            entity.HasOne(access => access.Account)
                .WithOne(account => account.OperatorAccess)
                .HasForeignKey<IdentityOperatorAccessEntity>(access => access.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrganizationEntity>(entity =>
        {
            entity.ToTable("organization", "org");
            entity.HasKey(organization => organization.Id);
            entity.Property(organization => organization.Id).HasColumnName("id");
            entity.Property(organization => organization.Name).HasColumnName("name").HasMaxLength(120);
            entity.Property(organization => organization.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(organization => organization.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<OrganizationMemberEntity>(entity =>
        {
            entity.ToTable("member", "org");
            entity.HasKey(member => new { member.OrganizationId, member.AccountId });
            entity.Property(member => member.OrganizationId).HasColumnName("organization_id");
            entity.Property(member => member.AccountId).HasColumnName("account_id");
            entity.Property(member => member.Role).HasColumnName("role").HasMaxLength(30);
            entity.Property(member => member.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(member => member.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(member => member.AccountId);
            entity.HasOne(member => member.Organization)
                .WithMany(organization => organization.Members)
                .HasForeignKey(member => member.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(member => member.Account)
                .WithMany()
                .HasForeignKey(member => member.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectEntity>(entity =>
        {
            entity.ToTable("project", "gateway");
            entity.HasKey(project => project.Id);
            entity.Property(project => project.Id).HasColumnName("id");
            entity.Property(project => project.OrganizationId).HasColumnName("organization_id");
            entity.Property(project => project.Name).HasColumnName("name").HasMaxLength(120);
            entity.Property(project => project.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(project => project.SettingsJson).HasColumnName("settings_json").HasColumnType("jsonb");
            entity.Property(project => project.CreatedAt).HasColumnName("created_at");
            entity.Property(project => project.ArchivedAt).HasColumnName("archived_at");
            entity.HasIndex(project => new { project.OrganizationId, project.Name }).IsUnique();
            entity.HasIndex(project => new { project.OrganizationId, project.Id }).IsUnique();
            entity.HasOne(project => project.Organization)
                .WithMany(organization => organization.Projects)
                .HasForeignKey(project => project.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OutboxMessageEntity>(entity =>
        {
            entity.ToTable("outbox", "ops");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).HasColumnName("id");
            entity.Property(message => message.EventType).HasColumnName("event_type").HasMaxLength(200);
            entity.Property(message => message.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(message => message.OccurredAt).HasColumnName("occurred_at");
            entity.Property(message => message.AvailableAt).HasColumnName("available_at");
            entity.Property(message => message.ProcessedAt).HasColumnName("processed_at");
            entity.Property(message => message.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(200);
            entity.Property(message => message.LeaseExpiresAt).HasColumnName("lease_expires_at");
            entity.Property(message => message.AttemptCount).HasColumnName("attempt_count");
            entity.Property(message => message.MaxAttempts).HasColumnName("max_attempts");
            entity.Property(message => message.DeadLetteredAt).HasColumnName("dead_lettered_at");
            entity.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(500);
            entity.HasIndex(message => new { message.ProcessedAt, message.AvailableAt, message.LeaseExpiresAt });
        });

        modelBuilder.Entity<ConsumerInboxEntryEntity>(entity =>
        {
            entity.ToTable("consumer_inbox", "ops");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).HasColumnName("id");
            entity.Property(entry => entry.Consumer).HasColumnName("consumer").HasMaxLength(200);
            entity.Property(entry => entry.EventId).HasColumnName("event_id");
            entity.Property(entry => entry.ProcessedAt).HasColumnName("processed_at");
            entity.HasIndex(entry => new { entry.Consumer, entry.EventId }).IsUnique();
        });

        modelBuilder.Entity<LeasedJobEntity>(entity =>
        {
            entity.ToTable("job", "ops");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Id).HasColumnName("id");
            entity.Property(job => job.JobType).HasColumnName("job_type").HasMaxLength(200);
            entity.Property(job => job.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(job => job.DeduplicationKey).HasColumnName("deduplication_key");
            entity.Property(job => job.AvailableAt).HasColumnName("available_at");
            entity.Property(job => job.CompletedAt).HasColumnName("completed_at");
            entity.Property(job => job.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(200);
            entity.Property(job => job.LeaseExpiresAt).HasColumnName("lease_expires_at");
            entity.Property(job => job.AttemptCount).HasColumnName("attempt_count");
            entity.Property(job => job.MaxAttempts).HasColumnName("max_attempts");
            entity.Property(job => job.DeadLetteredAt).HasColumnName("dead_lettered_at");
            entity.Property(job => job.LastError).HasColumnName("last_error").HasMaxLength(500);
            entity.HasIndex(job => new { job.CompletedAt, job.AvailableAt, job.LeaseExpiresAt });
            entity.HasIndex(job => new { job.JobType, job.DeduplicationKey }).IsUnique();
        });

        modelBuilder.Entity<OperationalAlertEntity>(entity =>
        {
            entity.ToTable("operational_alert", "ops");
            entity.HasKey(alert => alert.Id);
            entity.Property(alert => alert.Id).HasColumnName("id");
            entity.Property(alert => alert.Kind).HasColumnName("kind").HasMaxLength(100);
            entity.Property(alert => alert.Severity).HasColumnName("severity").HasMaxLength(30);
            entity.Property(alert => alert.DeduplicationKey).HasColumnName("deduplication_key").HasMaxLength(200);
            entity.Property(alert => alert.Details).HasColumnName("details").HasColumnType("jsonb");
            entity.Property(alert => alert.OccurredAt).HasColumnName("occurred_at");
            entity.Property(alert => alert.ResolvedAt).HasColumnName("resolved_at");
            entity.HasIndex(alert => new { alert.Kind, alert.OccurredAt });
        });
    }
}
