using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed partial class FoundationDbContext(DbContextOptions<FoundationDbContext> options) : DbContext(options)
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

    internal DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    internal DbSet<GatewayApiKeyEntity> GatewayApiKeys => Set<GatewayApiKeyEntity>();

    internal DbSet<BillingWalletEntity> BillingWallets => Set<BillingWalletEntity>();

    internal DbSet<BillingLedgerEntryEntity> BillingLedgerEntries => Set<BillingLedgerEntryEntity>();

    internal DbSet<BillingFeePolicyVersionEntity> BillingFeePolicyVersions => Set<BillingFeePolicyVersionEntity>();

    internal DbSet<BillingFxRateSnapshotEntity> BillingFxRateSnapshots => Set<BillingFxRateSnapshotEntity>();

    internal DbSet<PaymentQuoteEntity> PaymentQuotes => Set<PaymentQuoteEntity>();

    internal DbSet<PaymentIntentEntity> PaymentIntents => Set<PaymentIntentEntity>();

    internal DbSet<PaymentCallbackReceiptEntity> PaymentCallbackReceipts => Set<PaymentCallbackReceiptEntity>();

    internal DbSet<PaymentReconciliationCaseEntity> PaymentReconciliationCases => Set<PaymentReconciliationCaseEntity>();

    internal DbSet<CatalogProviderEntity> CatalogProviders => Set<CatalogProviderEntity>();

    internal DbSet<CatalogModelEntity> CatalogModels => Set<CatalogModelEntity>();

    internal DbSet<CatalogProviderModelEntity> CatalogProviderModels => Set<CatalogProviderModelEntity>();

    internal DbSet<CatalogModelPriceEntity> CatalogModelPrices => Set<CatalogModelPriceEntity>();

    internal DbSet<UsageRequestEntity> UsageRequests => Set<UsageRequestEntity>();

    internal DbSet<UsageIdempotencyClaimEntity> UsageIdempotencyClaims => Set<UsageIdempotencyClaimEntity>();

    internal DbSet<UsageAttemptEntity> UsageAttempts => Set<UsageAttemptEntity>();

    internal DbSet<UsageEvidenceEntity> UsageEvidence => Set<UsageEvidenceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureFinancialCompletion(modelBuilder);
        ConfigureProviderCredentials(modelBuilder);
        ConfigurePayments(modelBuilder);
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

        modelBuilder.Entity<GatewayApiKeyEntity>(entity =>
        {
            entity.ToTable("api_key", "gateway", table => table.HasCheckConstraint("CK_api_key_status", "status IN ('Active', 'Disabled')"));
            entity.HasKey(apiKey => apiKey.Id);
            entity.Property(apiKey => apiKey.Id).HasColumnName("id");
            entity.Property(apiKey => apiKey.ProjectId).HasColumnName("project_id");
            entity.Property(apiKey => apiKey.Name).HasColumnName("name").HasMaxLength(120);
            entity.Property(apiKey => apiKey.Prefix).HasColumnName("key_prefix").HasMaxLength(12);
            entity.Property(apiKey => apiKey.SecretFingerprint).HasColumnName("secret_fingerprint");
            entity.Property(apiKey => apiKey.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(apiKey => apiKey.ExpiresAt).HasColumnName("expires_at");
            entity.Property(apiKey => apiKey.CreatedByAccountId).HasColumnName("created_by");
            entity.Property(apiKey => apiKey.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(apiKey => apiKey.Prefix).IsUnique();
            entity.HasIndex(apiKey => new { apiKey.ProjectId, apiKey.CreatedAt }).IsDescending(false, true);
            entity.HasOne(apiKey => apiKey.Project).WithMany().HasForeignKey(apiKey => apiKey.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(apiKey => apiKey.CreatedByAccount).WithMany().HasForeignKey(apiKey => apiKey.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEventEntity>(entity =>
        {
            entity.ToTable("audit_event", "audit");
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.Property(auditEvent => auditEvent.Id).HasColumnName("id");
            entity.Property(auditEvent => auditEvent.OrganizationId).HasColumnName("organization_id");
            entity.Property(auditEvent => auditEvent.ActorAccountId).HasColumnName("account_id");
            entity.Property(auditEvent => auditEvent.Action).HasColumnName("action").HasMaxLength(200);
            entity.Property(auditEvent => auditEvent.ResourceType).HasColumnName("resource_type").HasMaxLength(100);
            entity.Property(auditEvent => auditEvent.ResourceId).HasColumnName("resource_id");
            entity.Property(auditEvent => auditEvent.IpAddress).HasColumnName("ip").HasColumnType("inet");
            entity.Property(auditEvent => auditEvent.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            entity.Property(auditEvent => auditEvent.OccurredAt).HasColumnName("occurred_at");
            entity.HasIndex(auditEvent => new { auditEvent.OrganizationId, auditEvent.OccurredAt }).IsDescending(false, true);
            entity.HasIndex(auditEvent => new { auditEvent.ActorAccountId, auditEvent.OccurredAt }).IsDescending(false, true);
            entity.HasOne(auditEvent => auditEvent.Organization)
                .WithMany()
                .HasForeignKey(auditEvent => auditEvent.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(auditEvent => auditEvent.ActorAccount)
                .WithMany()
                .HasForeignKey(auditEvent => auditEvent.ActorAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingWalletEntity>(entity =>
        {
            entity.ToTable("wallet", "billing", table => table.HasCheckConstraint("CK_wallet_non_negative", "posted_balance_micro_usd >= reserved_balance_micro_usd AND reserved_balance_micro_usd >= 0"));
            entity.HasKey(wallet => wallet.OrganizationId);
            entity.Property(wallet => wallet.OrganizationId).HasColumnName("organization_id");
            entity.Property(wallet => wallet.PostedBalanceMicroUsd).HasColumnName("posted_balance_micro_usd");
            entity.Property(wallet => wallet.ReservedBalanceMicroUsd).HasColumnName("reserved_balance_micro_usd");
            entity.Property(wallet => wallet.Version).HasColumnName("version");
            entity.HasOne(wallet => wallet.Organization)
                .WithMany()
                .HasForeignKey(wallet => wallet.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingLedgerEntryEntity>(entity =>
        {
            entity.ToTable("ledger_entry", "billing", table => table.HasCheckConstraint(
                "CK_ledger_entry_direction",
                "(amount_micro_usd > 0 AND type IN ('TopUp', 'Refund', 'AdjustmentCredit', 'PromotionalCredit')) OR (amount_micro_usd < 0 AND type IN ('UsageCharge', 'AdjustmentDebit'))"));
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).HasColumnName("id");
            entity.Property(entry => entry.OrganizationId).HasColumnName("organization_id");
            entity.Property(entry => entry.Type).HasColumnName("type").HasMaxLength(30);
            entity.Property(entry => entry.AmountMicroUsd).HasColumnName("amount_micro_usd");
            entity.Property(entry => entry.ReferenceType).HasColumnName("reference_type").HasMaxLength(100);
            entity.Property(entry => entry.ReferenceId).HasColumnName("reference_id");
            entity.Property(entry => entry.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            entity.Property(entry => entry.OccurredAt).HasColumnName("occurred_at");
            entity.HasIndex(entry => new { entry.OrganizationId, entry.Type, entry.ReferenceType, entry.ReferenceId }).IsUnique();
            entity.HasIndex(entry => new { entry.OrganizationId, entry.OccurredAt }).IsDescending(false, true);
            entity.HasOne(entry => entry.Organization)
                .WithMany()
                .HasForeignKey(entry => entry.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingFeePolicyVersionEntity>(entity =>
        {
            entity.ToTable("fee_policy_version", "billing", table =>
            {
                table.HasCheckConstraint("CK_fee_policy_version_range", "effective_to IS NULL OR effective_to > effective_from");
                table.HasCheckConstraint("CK_fee_policy_version_values", "markup_basis_points BETWEEN 0 AND 100000 AND fixed_fee_micro_usd >= 0");
            });
            entity.HasKey(version => version.Id);
            entity.Property(version => version.Id).HasColumnName("id");
            entity.Property(version => version.PolicyCode).HasColumnName("policy_code").HasMaxLength(100);
            entity.Property(version => version.MarkupBasisPoints).HasColumnName("markup_basis_points");
            entity.Property(version => version.FixedFeeMicroUsd).HasColumnName("fixed_fee_micro_usd");
            entity.Property(version => version.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(version => version.EffectiveTo).HasColumnName("effective_to");
            entity.Property(version => version.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(version => new { version.PolicyCode, version.EffectiveFrom }).IsUnique();
        });

        modelBuilder.Entity<BillingFxRateSnapshotEntity>(entity =>
        {
            entity.ToTable("fx_rate_snapshot", "billing", table => table.HasCheckConstraint("CK_fx_rate_snapshot_positive", "uzs_tiyin_per_usd > 0"));
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.Id).HasColumnName("id");
            entity.Property(snapshot => snapshot.Source).HasColumnName("source").HasMaxLength(100);
            entity.Property(snapshot => snapshot.UzsTiyinPerUsd).HasColumnName("uzs_tiyin_per_usd").HasPrecision(20, 8);
            entity.Property(snapshot => snapshot.ObservedAt).HasColumnName("observed_at");
            entity.HasIndex(snapshot => new { snapshot.Source, snapshot.ObservedAt });
        });

        modelBuilder.Entity<CatalogProviderEntity>(entity =>
        {
            entity.ToTable("provider", "catalog", table => table.HasCheckConstraint("CK_catalog_provider_status", "status IN ('Active', 'Disabled')"));
            entity.HasKey(provider => provider.Id);
            entity.Property(provider => provider.Id).HasColumnName("id");
            entity.Property(provider => provider.Code).HasColumnName("code").HasMaxLength(200);
            entity.Property(provider => provider.Name).HasColumnName("name").HasMaxLength(120);
            entity.Property(provider => provider.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(provider => provider.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(provider => provider.Code).IsUnique();
        });

        modelBuilder.Entity<CatalogModelEntity>(entity =>
        {
            entity.ToTable("model", "catalog", table =>
            {
                table.HasCheckConstraint("CK_catalog_model_limits", "context_length > 0 AND max_output_tokens > 0 AND max_output_tokens <= context_length");
                table.HasCheckConstraint("CK_catalog_model_status", "status IN ('Active', 'Disabled')");
            });
            entity.HasKey(model => model.Id);
            entity.Property(model => model.Id).HasColumnName("id");
            entity.Property(model => model.CanonicalCode).HasColumnName("canonical_code").HasMaxLength(200);
            entity.Property(model => model.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            entity.Property(model => model.ContextLength).HasColumnName("context_length");
            entity.Property(model => model.MaxOutputTokens).HasColumnName("max_output_tokens");
            entity.Property(model => model.CapabilitiesJson).HasColumnName("capabilities_json").HasColumnType("jsonb");
            entity.Property(model => model.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(model => model.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(model => model.CanonicalCode).IsUnique();
        });

        modelBuilder.Entity<CatalogProviderModelEntity>(entity =>
        {
            entity.ToTable("provider_model", "catalog", table => table.HasCheckConstraint("CK_catalog_provider_model_status", "status IN ('Active', 'Disabled')"));
            entity.HasKey(mapping => mapping.Id);
            entity.Property(mapping => mapping.Id).HasColumnName("id");
            entity.Property(mapping => mapping.ProviderId).HasColumnName("provider_id");
            entity.Property(mapping => mapping.ModelId).HasColumnName("model_id");
            entity.Property(mapping => mapping.UpstreamModelCode).HasColumnName("upstream_model_code").HasMaxLength(300);
            entity.Property(mapping => mapping.EndpointReference).HasColumnName("endpoint_reference").HasMaxLength(500);
            entity.Property(mapping => mapping.Status).HasColumnName("status").HasMaxLength(30);
            entity.Property(mapping => mapping.CapabilityOverridesJson).HasColumnName("capabilities_override_json").HasColumnType("jsonb");
            entity.Property(mapping => mapping.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(mapping => new { mapping.ProviderId, mapping.ModelId }).IsUnique();
            entity.HasIndex(mapping => new { mapping.ProviderId, mapping.UpstreamModelCode }).IsUnique();
            entity.HasOne(mapping => mapping.Provider).WithMany(provider => provider.ProviderModels).HasForeignKey(mapping => mapping.ProviderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(mapping => mapping.Model).WithMany(model => model.ProviderModels).HasForeignKey(mapping => mapping.ModelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CatalogModelPriceEntity>(entity =>
        {
            entity.ToTable("model_price", "catalog", table =>
            {
                table.HasCheckConstraint("CK_catalog_model_price_range", "effective_to IS NULL OR effective_to > effective_from");
                table.HasCheckConstraint("CK_catalog_model_price_values", "input_price_micro_usd_per_million >= 0 AND output_price_micro_usd_per_million >= 0 AND (cached_input_price_micro_usd_per_million IS NULL OR cached_input_price_micro_usd_per_million >= 0)");
            });
            entity.HasKey(price => price.Id);
            entity.Property(price => price.Id).HasColumnName("id");
            entity.Property(price => price.ProviderModelId).HasColumnName("provider_model_id");
            entity.Property(price => price.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(price => price.EffectiveTo).HasColumnName("effective_to");
            entity.Property(price => price.InputPriceMicroUsdPerMillion).HasColumnName("input_price_micro_usd_per_million");
            entity.Property(price => price.OutputPriceMicroUsdPerMillion).HasColumnName("output_price_micro_usd_per_million");
            entity.Property(price => price.CachedInputPriceMicroUsdPerMillion).HasColumnName("cached_input_price_micro_usd_per_million");
            entity.Property(price => price.ExtraPricingJson).HasColumnName("extra_pricing_json").HasColumnType("jsonb");
            entity.Property(price => price.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(price => new { price.ProviderModelId, price.EffectiveFrom }).IsUnique();
            entity.HasIndex(price => new { price.ProviderModelId, price.Id }).IsUnique();
            entity.HasOne(price => price.ProviderModel).WithMany(mapping => mapping.Prices).HasForeignKey(price => price.ProviderModelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UsageRequestEntity>(entity =>
        {
            entity.ToTable("request", "usage", table =>
            {
                table.HasCheckConstraint("CK_usage_request_execution", "execution_state IN ('Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown', 'RejectedBeforeExecution')");
                table.HasCheckConstraint("CK_usage_request_delivery", "delivery_state IN ('NotStarted', 'Partial', 'Completed', 'ClientDisconnected')");
                table.HasCheckConstraint("CK_usage_request_financial", "financial_state IN ('PendingAdmission', 'Reserved', 'PendingEvidence', 'PendingSettlement', 'Settled', 'Released')");
            });
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id).HasColumnName("id");
            entity.Property(request => request.OrganizationId).HasColumnName("organization_id");
            entity.Property(request => request.ProjectId).HasColumnName("project_id");
            entity.Property(request => request.ApiKeyId).HasColumnName("api_key_id");
            entity.Property(request => request.CanonicalModelId).HasColumnName("canonical_model_id");
            entity.Property(request => request.StartedAt).HasColumnName("started_at");
            entity.Property(request => request.CompletedAt).HasColumnName("completed_at");
            entity.Property(request => request.ExecutionState).HasColumnName("execution_state").HasMaxLength(30);
            entity.Property(request => request.DeliveryState).HasColumnName("delivery_state").HasMaxLength(30);
            entity.Property(request => request.FinancialState).HasColumnName("financial_state").HasMaxLength(30);
            entity.Property(request => request.IsStream).HasColumnName("is_stream");
            entity.Property(request => request.Operation).HasColumnName("operation").HasMaxLength(80);
            entity.Property(request => request.RouteStrategy).HasColumnName("route_strategy").HasMaxLength(80);
            entity.Property(request => request.TraceId).HasColumnName("trace_id").HasMaxLength(128);
            entity.Property(request => request.HttpStatus).HasColumnName("http_status");
            entity.HasIndex(request => new { request.OrganizationId, request.StartedAt }).IsDescending(false, true);
            entity.HasIndex(request => new { request.ProjectId, request.StartedAt }).IsDescending(false, true);
            entity.HasIndex(request => new { request.ApiKeyId, request.StartedAt }).IsDescending(false, true);
            entity.HasIndex(request => new { request.Id, request.OrganizationId, request.ApiKeyId }).IsUnique();
            entity.HasOne(request => request.Organization).WithMany().HasForeignKey(request => request.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(request => request.Project).WithMany().HasForeignKey(request => new { request.OrganizationId, request.ProjectId }).HasPrincipalKey(project => new { project.OrganizationId, project.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(request => request.ApiKey).WithMany().HasForeignKey(request => new { request.ProjectId, request.ApiKeyId }).HasPrincipalKey(apiKey => new { apiKey.ProjectId, apiKey.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(request => request.CanonicalModel).WithMany().HasForeignKey(request => request.CanonicalModelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UsageIdempotencyClaimEntity>(entity =>
        {
            entity.ToTable("idempotency_claim", "usage", table => table.HasCheckConstraint("CK_usage_idempotency_hashes", "octet_length(key_hash) = 32 AND octet_length(payload_hash) = 32"));
            entity.HasKey(claim => new { claim.OrganizationId, claim.ApiKeyId, claim.Operation, claim.KeyHash });
            entity.Property(claim => claim.OrganizationId).HasColumnName("organization_id");
            entity.Property(claim => claim.ApiKeyId).HasColumnName("api_key_id");
            entity.Property(claim => claim.Operation).HasColumnName("operation").HasMaxLength(80);
            entity.Property(claim => claim.KeyHash).HasColumnName("key_hash");
            entity.Property(claim => claim.PayloadHash).HasColumnName("payload_hash");
            entity.Property(claim => claim.RequestId).HasColumnName("request_id");
            entity.Property(claim => claim.ExpiresAt).HasColumnName("expires_at");
            entity.HasIndex(claim => claim.ExpiresAt);
            entity.HasOne(claim => claim.Request).WithMany().HasForeignKey(claim => new { claim.RequestId, claim.OrganizationId, claim.ApiKeyId }).HasPrincipalKey(request => new { request.Id, request.OrganizationId, request.ApiKeyId }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UsageAttemptEntity>(entity =>
        {
            entity.ToTable("attempt", "usage", table => table.HasCheckConstraint("CK_usage_attempt_execution", "execution_state IN ('Prepared', 'Dispatched', 'Succeeded', 'Failed', 'Canceled', 'OutcomeUnknown', 'RejectedBeforeExecution')"));
            entity.HasKey(attempt => attempt.Id);
            entity.Property(attempt => attempt.Id).HasColumnName("id");
            entity.Property(attempt => attempt.RequestId).HasColumnName("request_id");
            entity.Property(attempt => attempt.Number).HasColumnName("number");
            entity.Property(attempt => attempt.ProviderModelId).HasColumnName("provider_model_id");
            entity.Property(attempt => attempt.StartedAt).HasColumnName("started_at");
            entity.Property(attempt => attempt.CompletedAt).HasColumnName("completed_at");
            entity.Property(attempt => attempt.ExecutionState).HasColumnName("execution_state").HasMaxLength(30);
            entity.Property(attempt => attempt.ProviderRequestId).HasColumnName("provider_request_id").HasMaxLength(200);
            entity.Property(attempt => attempt.ErrorCategory).HasColumnName("error_category").HasMaxLength(100);
            entity.HasIndex(attempt => new { attempt.RequestId, attempt.Number }).IsUnique();
            entity.HasIndex(attempt => new { attempt.Id, attempt.RequestId }).IsUnique();
            entity.HasOne(attempt => attempt.Request).WithMany(request => request.Attempts).HasForeignKey(attempt => attempt.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(attempt => attempt.ProviderModel).WithMany().HasForeignKey(attempt => attempt.ProviderModelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UsageEvidenceEntity>(entity =>
        {
            entity.ToTable("evidence", "usage", table =>
            {
                table.HasCheckConstraint("CK_usage_evidence_source", "source IN ('Provider', 'Estimated', 'Reconciled', 'Unknown')");
                table.HasCheckConstraint("CK_usage_evidence_state", "(state = 'Unknown' AND source = 'Unknown' AND input_tokens IS NULL AND output_tokens IS NULL AND price_version_id IS NULL AND reconcile_after IS NOT NULL) OR (state = 'Verified' AND source IN ('Provider', 'Estimated', 'Reconciled') AND input_tokens >= 0 AND output_tokens >= 0 AND cached_input_tokens >= 0 AND cached_input_tokens <= input_tokens AND (reasoning_tokens IS NULL OR (reasoning_tokens >= 0 AND reasoning_tokens <= output_tokens)) AND price_version_id IS NOT NULL AND reconcile_after IS NULL)");
            });
            entity.HasKey(evidence => evidence.Id);
            entity.Property(evidence => evidence.Id).HasColumnName("id");
            entity.Property(evidence => evidence.RequestId).HasColumnName("request_id");
            entity.Property(evidence => evidence.AttemptId).HasColumnName("attempt_id");
            entity.Property(evidence => evidence.ProviderModelId).HasColumnName("provider_model_id");
            entity.Property(evidence => evidence.State).HasColumnName("state").HasMaxLength(30);
            entity.Property(evidence => evidence.Source).HasColumnName("source").HasMaxLength(30);
            entity.Property(evidence => evidence.InputTokens).HasColumnName("input_tokens");
            entity.Property(evidence => evidence.OutputTokens).HasColumnName("output_tokens");
            entity.Property(evidence => evidence.CachedInputTokens).HasColumnName("cached_input_tokens");
            entity.Property(evidence => evidence.ReasoningTokens).HasColumnName("reasoning_tokens");
            entity.Property(evidence => evidence.PriceVersionId).HasColumnName("price_version_id");
            entity.Property(evidence => evidence.ProviderRequestId).HasColumnName("provider_request_id").HasMaxLength(200);
            entity.Property(evidence => evidence.CapturedAt).HasColumnName("captured_at");
            entity.Property(evidence => evidence.ReconcileAfter).HasColumnName("reconcile_after");
            entity.HasIndex(evidence => new { evidence.AttemptId, evidence.State }).IsUnique();
            entity.HasIndex(evidence => new { evidence.RequestId, evidence.CapturedAt });
            entity.HasOne(evidence => evidence.Request).WithMany().HasForeignKey(evidence => evidence.RequestId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(evidence => evidence.Attempt).WithMany().HasForeignKey(evidence => new { evidence.AttemptId, evidence.RequestId }).HasPrincipalKey(attempt => new { attempt.Id, attempt.RequestId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(evidence => evidence.PriceVersion).WithMany()
                .HasForeignKey(evidence => new { evidence.ProviderModelId, evidence.PriceVersionId })
                .HasPrincipalKey(price => new { price.ProviderModelId, price.Id })
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
