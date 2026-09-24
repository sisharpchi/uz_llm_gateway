using UZLLM.Observability;
using UZLLM.Persistence;
using UZLLM.Modules.ApiKeys.Infrastructure;
using UZLLM.Modules.Catalog.Infrastructure;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Usage.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddUzllmPersistence(builder.Configuration);
builder.Services.AddUzllmRedis(builder.Configuration);
builder.Services.AddUzllmObservability(builder.Configuration, "UZLLM.Gateway.Api");
builder.Services.AddUzllmReadinessChecks();
builder.Services.AddUzllmApiKeys(builder.Configuration);
builder.Services.AddUzllmCatalog();
builder.Services.AddUzllmUsage();
builder.Services.AddUzllmBilling();
var app = builder.Build();

app.UseUzllmRequestCorrelation();
app.MapUzllmHealthEndpoints();
app.Run();
