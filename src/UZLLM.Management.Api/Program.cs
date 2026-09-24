using UZLLM.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddUzllmPersistence(builder.Configuration);
builder.Services.AddUzllmRedis(builder.Configuration);
var app = builder.Build();

app.Run();
