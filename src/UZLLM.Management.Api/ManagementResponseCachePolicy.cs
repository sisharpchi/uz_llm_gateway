namespace UZLLM.Management.Api;

/// <summary>Prevents browser and intermediary storage of control-plane responses.</summary>
public static class ManagementResponseCachePolicy
{
    public static IApplicationBuilder UseUzllmManagementNoStore(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/management/v1", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    return Task.CompletedTask;
                });
            }

            await next(context);
        });
}
