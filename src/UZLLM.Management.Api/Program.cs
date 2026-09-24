using UZLLM.Observability;
using UZLLM.Persistence;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Audit.Infrastructure;
using UZLLM.Modules.Billing.Infrastructure;
using UZLLM.Modules.Organizations.Infrastructure;
using UZLLM.Modules.Projects.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddUzllmPersistence(builder.Configuration);
builder.Services.AddUzllmRedis(builder.Configuration);
builder.Services.AddUzllmObservability(builder.Configuration, "UZLLM.Management.Api");
builder.Services.AddUzllmReadinessChecks();
builder.Services.AddUzllmIdentity();
builder.Services.AddUzllmAudit();
builder.Services.AddUzllmBilling();
builder.Services.AddUzllmOrganizations();
builder.Services.AddUzllmProjects();
var app = builder.Build();

app.UseUzllmRequestCorrelation();
app.UseUzllmManagementSession();
app.MapUzllmHealthEndpoints();
app.MapUzllmIdentityEndpoints();
app.MapUzllmOrganizationEndpoints();
app.MapUzllmProjectEndpoints();
app.Run();
