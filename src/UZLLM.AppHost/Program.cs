using System.Diagnostics;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

// This AppHost runs only disposable local fixtures. Production topology remains in deploy/.
var builder = DistributedApplication.CreateBuilder(args);
var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
var certDirectory = Path.Combine(Path.GetTempPath(), $"uzllm-aspire-{Guid.NewGuid():N}");
Directory.CreateDirectory(certDirectory);
var certPath = Path.Combine(certDirectory, "localhost.crt");
var keyPath = Path.Combine(certDirectory, "localhost.key");
using (var export = Process.Start(new ProcessStartInfo("dotnet")
{
    ArgumentList = { "dev-certs", "https", "--export-path", certPath, "--format", "PEM", "--no-password" },
    RedirectStandardError = true,
    RedirectStandardOutput = true,
    UseShellExecute = false
}) ?? throw new InvalidOperationException("Could not start the .NET development certificate export."))
{
    await export.WaitForExitAsync();
    if (export.ExitCode != 0 || !File.Exists(certPath) || !File.Exists(keyPath))
        throw new InvalidOperationException("A trusted, exportable .NET HTTPS development certificate is required. Run 'dotnet dev-certs https --trust'.");
}

static void DeleteExport(string path)
{
    if (!Directory.Exists(path)) return;
    try { Directory.Delete(path, recursive: true); }
    catch (IOException) { } // A stopped container may briefly retain its bind mount.
    catch (UnauthorizedAccessException) { }
}
AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteExport(certDirectory);

static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
foreach (var key in new[] { "migrator-password", "runtime-password", "fingerprint-key", "provider-key", "payload-key", "payme-key", "click-key" })
    builder.Configuration[$"Parameters:{key}"] ??= NewSecret();
builder.Configuration["Parameters:migrator-user"] ??= "uzllm_migrator";

var migratorUser = builder.AddParameter("migrator-user");
var migratorPassword = builder.AddParameter("migrator-password", secret: true);
var runtimePassword = builder.AddParameter("runtime-password", secret: true);
var fingerprintKey = builder.AddParameter("fingerprint-key", secret: true);
var providerKey = builder.AddParameter("provider-key", secret: true);
var payloadKey = builder.AddParameter("payload-key", secret: true);
var paymeKey = builder.AddParameter("payme-key", secret: true);
var clickKey = builder.AddParameter("click-key", secret: true);

var postgres = builder.AddPostgres("postgres", migratorUser, migratorPassword)
    .WithImageTag("17-alpine")
    .WithEnvironment("POSTGRES_DB", "uzllm_acceptance")
    .WithEnvironment("UZLLM_RUNTIME_DB_USER", "uzllm_runtime")
    .WithEnvironment("UZLLM_RUNTIME_DB_PASSWORD", runtimePassword)
    .WithInitFiles(Path.Combine(repositoryRoot, "docker", "postgres", "init"));
var redis = builder.AddRedis("redis").WithImageTag("8-alpine");

var pgEndpoint = postgres.GetEndpoint("tcp");
var migratorDsn = ReferenceExpression.Create($"Host={pgEndpoint.Property(EndpointProperty.Host)};Port={pgEndpoint.Property(EndpointProperty.Port)};Database=uzllm_acceptance;Username={migratorUser};Password={migratorPassword};SSL Mode=Disable");
var runtimeDsn = ReferenceExpression.Create($"Host={pgEndpoint.Property(EndpointProperty.Host)};Port={pgEndpoint.Property(EndpointProperty.Port)};Database=uzllm_acceptance;Username=uzllm_runtime;Password={runtimePassword};SSL Mode=Disable");

var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.27.4")
    .WithBindMount(certDirectory, "/run/certs", isReadOnly: true)
    .WithEnvironment("MP_SMTP_TLS_CERT", "/run/certs/localhost.crt")
    .WithEnvironment("MP_SMTP_TLS_KEY", "/run/certs/localhost.key")
    .WithEnvironment("MP_SMTP_REQUIRE_STARTTLS", "true")
    .WithHttpEndpoint(targetPort: 8025, name: "http")
    .WithEndpoint(targetPort: 1025, name: "smtp", scheme: "tcp");

var provider = builder.AddNodeApp("provider-fixture", Path.Combine(repositoryRoot, "deploy", "load"), "deterministic-upstream.mjs")
    .WithHttpsEndpoint(env: "UZLLM_FIXTURE_PORT")
    .WithEnvironment("UZLLM_FIXTURE_CERT", certPath)
    .WithEnvironment("UZLLM_FIXTURE_KEY", keyPath)
    .WithEnvironment("UZLLM_FIXTURE_DURATION_MS", "1000");

var migrator = builder.AddProject<Projects.UZLLM_Migrator>("migrator", launchProfileName: null)
    .WithEnvironment("ConnectionStrings__Postgres", migratorDsn)
    .WaitFor(postgres);
var seed = builder.AddProject<Projects.UZLLM_AcceptanceSeed>("fixture-seed", launchProfileName: null)
    .WithEnvironment("ConnectionStrings__Postgres", migratorDsn)
    .WithEnvironment("ProviderSecrets__ActiveKeyVersion", "v1")
    .WithEnvironment("ProviderSecrets__Keys__v1", providerKey)
    .WithEnvironment("UZLLM_ACCEPTANCE_SEED_ALLOW", "disposable-stack")
    .WaitForCompletion(migrator);

var management = builder.AddProject<Projects.UZLLM_Management_Api>("management", launchProfileName: null)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Postgres", runtimeDsn)
    .WithReference(redis, "Redis")
    .WithEnvironment("ApiKeys__FingerprintKey", fingerprintKey)
    .WithEnvironment("ApiKeys__ActiveKeyVersion", "1")
    .WithEnvironment("ProviderSecrets__ActiveKeyVersion", "v1")
    .WithEnvironment("ProviderSecrets__Keys__v1", providerKey)
    .WithEnvironment("PayloadSecrets__ActiveKeyVersion", "v1")
    .WithEnvironment("PayloadSecrets__Keys__v1", payloadKey)
    .WithEnvironment("Payments__Payme__MerchantId", "local-aspire-payme")
    .WithEnvironment("Payments__Payme__Key", paymeKey)
    .WithEnvironment("Payments__Click__MerchantId", "local-aspire-click")
    .WithEnvironment("Payments__Click__ServiceId", "local-aspire-service")
    .WithEnvironment("Payments__Click__SecretKey", clickKey)
    .WaitForCompletion(seed)
    .WaitFor(redis);

var gateway = builder.AddProject<Projects.UZLLM_Gateway_Api>("gateway", launchProfileName: null)
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Postgres", runtimeDsn)
    .WithReference(redis, "Redis")
    .WithEnvironment("ApiKeys__FingerprintKey", fingerprintKey)
    .WithEnvironment("ApiKeys__ActiveKeyVersion", "1")
    .WithEnvironment("ProviderSecrets__ActiveKeyVersion", "v1")
    .WithEnvironment("ProviderSecrets__Keys__v1", providerKey)
    .WithEnvironment("PayloadSecrets__ActiveKeyVersion", "v1")
    .WithEnvironment("PayloadSecrets__Keys__v1", payloadKey)
    .WithEnvironment("Providers__OpenAI__BaseUrl", ReferenceExpression.Create($"{provider.GetEndpoint("https")}/v1/"))
    .WaitForCompletion(seed)
    .WaitFor(redis)
    .WaitFor(provider);

var worker = builder.AddProject<Projects.UZLLM_Worker>("worker", launchProfileName: null)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("ConnectionStrings__Postgres", runtimeDsn)
    .WithReference(redis, "Redis")
    .WithEnvironment("Email__SmtpHost", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
    .WithEnvironment("Email__SmtpPort", mailpit.GetEndpoint("smtp").Property(EndpointProperty.Port))
    .WithEnvironment("Email__FromAddress", "no-reply@aspire.invalid")
    .WithEnvironment("Email__DashboardBaseUrl", "http://localhost")
    .WithEnvironment("Operations__AlertEmail", "oncall@aspire.invalid")
    .WaitForCompletion(seed)
    .WaitFor(redis)
    .WaitFor(mailpit);

var frontendDirectory = Path.Combine(repositoryRoot, "frontend", "apps");
var frontendInstall = builder.AddExecutable("frontend-install", "npm", frontendDirectory, "ci");
builder.AddViteApp("dashboard", frontendDirectory, "dev:dashboard")
    .WithNpm(install: false)
    .WithEnvironment("UZLLM_MANAGEMENT_PROXY_TARGET", management.GetEndpoint("http"))
    .WaitForCompletion(frontendInstall)
    .WaitFor(management);
builder.AddViteApp("admin", frontendDirectory, "dev:admin")
    .WithNpm(install: false)
    .WithEnvironment("UZLLM_MANAGEMENT_PROXY_TARGET", management.GetEndpoint("http"))
    .WaitForCompletion(frontendInstall)
    .WaitFor(management);

try
{
    await using var application = builder.Build();
    await application.RunAsync();
}
finally
{
    for (var attempt = 0; attempt < 8 && Directory.Exists(certDirectory); attempt++)
    {
        DeleteExport(certDirectory);
        if (Directory.Exists(certDirectory)) await Task.Delay(250);
    }
}
