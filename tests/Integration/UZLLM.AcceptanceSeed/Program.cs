using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Persistence;

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var dsn = configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Postgres.");
if (configuration["UZLLM_ACCEPTANCE_SEED_ALLOW"] != "disposable-stack"
    || new NpgsqlConnectionStringBuilder(dsn).Database != "uzllm_acceptance")
    throw new InvalidOperationException("Acceptance seed refuses a non-disposable database.");

using var services = new ServiceCollection().AddUzllmPersistence(configuration).BuildServiceProvider();
await using var scope = services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
if (await db.Set<CatalogProviderEntity>().AnyAsync()
    || await db.Set<BillingFxRateSnapshotEntity>().AnyAsync())
    throw new InvalidOperationException("Acceptance seed requires an empty migrated catalog and FX table.");

var now = DateTimeOffset.UtcNow;
var providerId = Guid.CreateVersion7();
var modelId = Guid.CreateVersion7();
var mappingId = Guid.CreateVersion7();
var credentialId = Guid.CreateVersion7();
var encrypted = new ProviderEnvelopeSecretProtector(configuration)
    .Protect(credentialId, providerId, "synthetic-acceptance-only");
db.Set<CatalogProviderEntity>().Add(new CatalogProviderEntity
{
    Id = providerId, Code = "openai", Name = "Deterministic acceptance provider",
    Status = "Active", CreatedAt = now
});
db.Set<CatalogModelEntity>().Add(new CatalogModelEntity
{
    Id = modelId, CanonicalCode = "fixture-model", DisplayName = "Fixture model",
    ContextLength = 8192, MaxOutputTokens = 512,
    CapabilitiesJson = "[\"Text\"]", Status = "Active", CreatedAt = now
});
db.Set<CatalogProviderModelEntity>().Add(new CatalogProviderModelEntity
{
    Id = mappingId, ProviderId = providerId, ModelId = modelId,
    UpstreamModelCode = "fixture-model", Status = "Active",
    CapabilityOverridesJson = "[]", CreatedAt = now
});
db.Set<CatalogModelPriceEntity>().Add(new CatalogModelPriceEntity
{
    Id = Guid.CreateVersion7(), ProviderModelId = mappingId,
    EffectiveFrom = now.AddMinutes(-1), InputPriceMicroUsdPerMillion = 1_000_000,
    OutputPriceMicroUsdPerMillion = 1_000_000,
    ExtraPricingJson = "{}", CreatedAt = now
});
var embeddingModelId = Guid.CreateVersion7();
var embeddingMappingId = Guid.CreateVersion7();
db.Set<CatalogModelEntity>().Add(new CatalogModelEntity
{
    Id = embeddingModelId, CanonicalCode = "fixture-embedding", DisplayName = "Fixture embedding",
    ContextLength = 8192, MaxOutputTokens = 0, CapabilitiesJson = "[\"Embeddings\"]", Status = "Active", CreatedAt = now
});
db.Set<CatalogProviderModelEntity>().Add(new CatalogProviderModelEntity
{
    Id = embeddingMappingId, ProviderId = providerId, ModelId = embeddingModelId,
    UpstreamModelCode = "fixture-embedding", Status = "Active", CapabilityOverridesJson = "[]", CreatedAt = now
});
db.Set<CatalogModelPriceEntity>().Add(new CatalogModelPriceEntity
{
    Id = Guid.CreateVersion7(), ProviderModelId = embeddingMappingId, EffectiveFrom = now.AddMinutes(-1),
    InputPriceMicroUsdPerMillion = 1_000_000, OutputPriceMicroUsdPerMillion = 0, ExtraPricingJson = "{}", CreatedAt = now
});
db.Set<BillingFeePolicyVersionEntity>().Add(new BillingFeePolicyVersionEntity
{
    Id = Guid.CreateVersion7(), PolicyCode = "default", MarkupBasisPoints = 0,
    FixedFeeMicroUsd = 0, EffectiveFrom = now.AddMinutes(-1), CreatedAt = now
});
// Synthetic fee-free BYOK lets the browser acceptance exercise the real
// encrypted resolver and settlement without a live supplier or funded wallet.
db.Set<BillingFeePolicyVersionEntity>().Add(new BillingFeePolicyVersionEntity
{
    Id = Guid.CreateVersion7(), PolicyCode = "byok", MarkupBasisPoints = 0,
    FixedFeeMicroUsd = 0, EffectiveFrom = now.AddMinutes(-1), CreatedAt = now
});
db.Set<BillingFxRateSnapshotEntity>().Add(new BillingFxRateSnapshotEntity
{
    Id = Guid.CreateVersion7(), Source = "LAUNCH-002 synthetic acceptance rate",
    UzsTiyinPerUsd = 1_000_000m, ObservedAt = now
});
db.Set<ProviderCredentialEntity>().Add(new ProviderCredentialEntity
{
    Id = credentialId, ProviderId = providerId, CredentialType = "Platform",
    Status = "Active", EncryptedSecret = encrypted.EncryptedSecret,
    WrappedDataKey = encrypted.WrappedDataKey, KeyVersion = encrypted.KeyVersion,
    CreatedAt = now
});
await db.SaveChangesAsync();
Console.WriteLine("Seeded synthetic provider, model, price, fee policy, and FX snapshot.");
