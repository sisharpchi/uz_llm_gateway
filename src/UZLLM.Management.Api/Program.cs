using UZLLM.Observability;
using UZLLM.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddUzllmPersistence(builder.Configuration);
builder.Services.AddUzllmRedis(builder.Configuration);
builder.Services.AddUzllmObservability(builder.Configuration, "UZLLM.Management.Api");
builder.Services.AddUzllmReadinessChecks();
var app = builder.Build();

app.UseUzllmRequestCorrelation();
app.MapUzllmHealthEndpoints();
app.Run();
