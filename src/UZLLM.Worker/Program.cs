using UZLLM.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddUzllmPersistence(builder.Configuration);
builder.Services.AddUzllmRedis(builder.Configuration);
var host = builder.Build();
await host.RunAsync();
