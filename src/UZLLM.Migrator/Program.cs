using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UZLLM.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddUzllmPersistence(builder.Configuration);

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();

var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
await migrator.MigrateAsync();
