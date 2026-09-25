using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.ApiKeys.Infrastructure;
using UZLLM.Modules.Billing.Application;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Persistence;

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var rawDsn = configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Postgres.");
var database = new NpgsqlConnectionStringBuilder(rawDsn).Database;
if (configuration["UZLLM_LOAD_SEED_ALLOW"] != "isolated-test-db"
    || database is null || !database.StartsWith("uzllm_load_", StringComparison.Ordinal))
    throw new InvalidOperationException("Load seed accepts only an isolated uzllm_load_* database.");

var services = new ServiceCollection().AddUzllmPersistence(configuration).BuildServiceProvider();
await using var scope = services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
if (await db.Set<CatalogProviderEntity>().AnyAsync())
    throw new InvalidOperationException("Load seed requires an empty migrated test database.");

var now = DateTimeOffset.UtcNow;
var accountId = Guid.CreateVersion7();
var organizationId = Guid.CreateVersion7();
var projectId = Guid.CreateVersion7();
var apiKeyId = Guid.CreateVersion7();
var providerId = Guid.CreateVersion7();
var modelId = Guid.CreateVersion7();
var mappingId = Guid.CreateVersion7();
var credentialId = Guid.CreateVersion7();
var secret = new GatewayApiKeySecretGenerator().Create();
var fingerprint = HmacApiKeySecretFingerprint.FromConfiguration(configuration).Create(secret.Value);
var encrypted = new ProviderEnvelopeSecretProtector(configuration)
    .Protect(credentialId, providerId, "fixture-only-key");

db.Set<IdentityAccountEntity>().Add(new IdentityAccountEntity
{
    Id = accountId, Email = $"load-{accountId:N}@example.invalid", PasswordHash = "load-only-no-login",
    Status = "Active", CreatedAt = now, UpdatedAt = now
});
db.Set<OrganizationEntity>().Add(new OrganizationEntity
{
    Id = organizationId, Name = "Isolated load drill", Status = "Active", CreatedAt = now
});
db.Set<ProjectEntity>().Add(new ProjectEntity
{
    Id = projectId, OrganizationId = organizationId, Name = "Load", Status = "Active",
    SettingsJson = "{}", CreatedAt = now
});
db.Set<GatewayApiKeyEntity>().Add(new GatewayApiKeyEntity
{
    Id = apiKeyId, ProjectId = projectId, Name = "Load key", Prefix = secret.Prefix,
    SecretFingerprint = fingerprint, Status = "Active", CreatedByAccountId = accountId, CreatedAt = now
});
db.Set<CatalogProviderEntity>().Add(new CatalogProviderEntity
{
    Id = providerId, Code = "openai", Name = "Deterministic fixture", Status = "Active", CreatedAt = now
});
db.Set<CatalogModelEntity>().Add(new CatalogModelEntity
{
    Id = modelId, CanonicalCode = "fixture-model", DisplayName = "Fixture model",
    ContextLength = 8192, MaxOutputTokens = 512, CapabilitiesJson = "[\"Text\"]",
    Status = "Active", CreatedAt = now
});
db.Set<CatalogProviderModelEntity>().Add(new CatalogProviderModelEntity
{
    Id = mappingId, ProviderId = providerId, ModelId = modelId, UpstreamModelCode = "fixture-model",
    Status = "Active", CapabilityOverridesJson = "[]", CreatedAt = now
});
db.Set<CatalogModelPriceEntity>().Add(new CatalogModelPriceEntity
{
    Id = Guid.CreateVersion7(), ProviderModelId = mappingId, EffectiveFrom = now.AddMinutes(-1),
    InputPriceMicroUsdPerMillion = 1_000_000, OutputPriceMicroUsdPerMillion = 1_000_000,
    ExtraPricingJson = "{}", CreatedAt = now
});
db.Set<BillingFeePolicyVersionEntity>().Add(new BillingFeePolicyVersionEntity
{
    Id = Guid.CreateVersion7(), PolicyCode = "default", MarkupBasisPoints = 0,
    FixedFeeMicroUsd = 0, EffectiveFrom = now.AddMinutes(-1), CreatedAt = now
});
db.Set<ProviderCredentialEntity>().Add(new ProviderCredentialEntity
{
    Id = credentialId, ProviderId = providerId, CredentialType = "Platform", Status = "Active",
    EncryptedSecret = encrypted.EncryptedSecret, WrappedDataKey = encrypted.WrappedDataKey,
    KeyVersion = encrypted.KeyVersion, CreatedAt = now
});
await db.SaveChangesAsync();
var wallet = new WalletLedgerService(new PostgreSqlWalletLedgerStore(db),
    scope.ServiceProvider.GetRequiredService<ITransactionCoordinator>(), TimeProvider.System,
    new PostgreSqlFinancialStore(db));
var credit = await wallet.PostAsync(new LedgerPostingInput(organizationId, LedgerEntryType.TopUp,
    new SignedUsdMicroAmount(1_000_000_000), "load_fixture", Guid.CreateVersion7()));
if (credit.Status != LedgerPostingStatus.Posted)
    throw new InvalidOperationException("Load wallet credit failed.");
Console.WriteLine($"Model: fixture-model\nOrganization: {organizationId}\nAPI key (show once): {secret.Value}");
