using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Providers.Infrastructure;
using UZLLM.Persistence;

var rotateSecrets = args.Length > 0 && args[0] == "--rotate-provider-secrets";
var rotateSession = args.Length > 0 && args[0] == "--rotate-session-key";
var builder = Host.CreateApplicationBuilder(rotateSecrets || rotateSession ? [] : args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
builder.Services.AddUzllmPersistence(builder.Configuration);
if (rotateSecrets || rotateSession)
{
    builder.Services.AddUzllmAudit();
}
if (rotateSecrets)
{
    builder.Services.AddUzllmProviders(builder.Configuration);
}
if (rotateSession)
{
    builder.Services.AddUzllmOperatorKeyRing(builder.Configuration, requireSharedRing: true);
    builder.Services.AddScoped<OperatorKeyRingRotation>();
}

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();

if (rotateSecrets)
{
    if (args.Length != 3 || !Guid.TryParse(args[1], out var actorId)
        || !int.TryParse(args[2], out var limit))
        throw new ArgumentException("Usage: --rotate-provider-secrets <operator-account-id> <limit:1..500>");
    var count = await scope.ServiceProvider.GetRequiredService<ProviderCredentialKeyRotation>()
        .RotateBatchAsync(actorId, limit);
    Console.WriteLine($"Rotated {count} provider credentials in this bounded batch.");
    return;
}
if (rotateSession)
{
    if (args.Length != 2 || !Guid.TryParse(args[1], out var actorId) || actorId == Guid.Empty)
        throw new ArgumentException("Usage: --rotate-session-key <operator-account-id>");
    var keyId = await scope.ServiceProvider.GetRequiredService<OperatorKeyRingRotation>()
        .RotateAsync(actorId);
    Console.WriteLine($"Created Data Protection key {keyId}; verify every node before retirement.");
    return;
}
if (args.Length != 0) throw new ArgumentException("Unknown migrator command.");
var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
await migrator.MigrateAsync();
