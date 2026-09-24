using UZLLM.Observability;
using UZLLM.Persistence;
using UZLLM.Modules.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddUzllmPersistence(builder.Configuration);
builder.Services.AddUzllmRedis(builder.Configuration);
builder.Services.AddUzllmObservability(builder.Configuration, "UZLLM.Management.Api");
builder.Services.AddUzllmReadinessChecks();
builder.Services.AddUzllmIdentity();
var app = builder.Build();

app.UseUzllmRequestCorrelation();
app.UseUzllmManagementSession();
app.MapUzllmHealthEndpoints();
app.MapUzllmIdentityEndpoints();
app.Run();
