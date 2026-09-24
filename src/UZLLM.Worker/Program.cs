using UZLLM.Observability;
using UZLLM.Persistence;
using UZLLM.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddUzllmPersistence(builder.Configuration);
builder.Services.AddUzllmRedis(builder.Configuration);
builder.Services.AddUzllmObservability(builder.Configuration, "UZLLM.Worker");
builder.Services.AddScoped<OutboxDispatchCycle>();
builder.Services.AddScoped<LeasedJobDispatchCycle>();
builder.Services.AddHostedService<OutboxDispatchWorker>();
builder.Services.AddHostedService<LeasedJobDispatchWorker>();
var host = builder.Build();
await host.RunAsync();
