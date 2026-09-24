using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed class FoundationDbContext(DbContextOptions<FoundationDbContext> options) : DbContext(options)
{
    internal DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    internal DbSet<ConsumerInboxEntryEntity> ConsumerInboxEntries => Set<ConsumerInboxEntryEntity>();

    internal DbSet<LeasedJobEntity> LeasedJobs => Set<LeasedJobEntity>();

    internal DbSet<OperationalAlertEntity> OperationalAlerts => Set<OperationalAlertEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
