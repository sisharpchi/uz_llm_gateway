using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UZLLM.Modules.ApiKeys.Application;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Identity.Infrastructure;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Modules.ApiKeys.Infrastructure;

public static class ApiKeyServiceCollectionExtensions
{
    public static IServiceCollection AddUzllmApiKeys(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IApiKeyStore, PostgreSqlApiKeyStore>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IApiKeyAuthenticator, ApiKeyAuthenticator>();
        services.AddSingleton<IApiKeySecretGenerator, GatewayApiKeySecretGenerator>();
        services.AddSingleton<IApiKeySecretFingerprint>(_ => HmacApiKeySecretFingerprint.FromConfiguration(configuration));
        var maxLeaseSeconds = configuration.GetValue<int?>("Limits:MaxLeaseSeconds") ?? 900;
        if (maxLeaseSeconds is < 1 or > 3600)
            throw new InvalidOperationException("Limits:MaxLeaseSeconds must be between 1 and 3600.");
        services.AddSingleton(new LimitRecoveryOptions(TimeSpan.FromSeconds(maxLeaseSeconds)));
        services.AddSingleton<IRedisEpochSource, RedisServerEpochSource>();
        services.AddSingleton<RedisAdmissionLimiter>();
        services.AddSingleton<IDistributedAdmissionLimiter>(provider => provider.GetRequiredService<RedisAdmissionLimiter>());
        services.AddSingleton<IProviderQuotaProtection>(provider => provider.GetRequiredService<RedisAdmissionLimiter>());
        services.AddSingleton<IRequestConstraintValidator, RequestConstraintValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapUzllmApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var projectKeys = endpoints.MapGroup("/management/v1/projects/{projectId:guid}/api-keys");
        projectKeys.MapGet("", async (Guid projectId, HttpContext context, IApiKeyService service, CancellationToken cancellationToken) =>
        {
            if (!TryGetAccountId(context.User, out var accountId)) return Results.Unauthorized();
            try { return Results.Ok(await service.ListAsync(accountId, projectId, cancellationToken)); }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        projectKeys.MapPost("", async (Guid projectId, CreateApiKeyRequest request, HttpContext context, IApiKeyService service, CancellationToken cancellationToken) =>
        {
            if (!TryGetAccountId(context.User, out var accountId)) return Results.Unauthorized();
            try
            {
                var issued = await service.CreateAsync(accountId, projectId, request.Name, request.ExpiresAt, cancellationToken);
                return Results.Created($"/management/v1/api-keys/{issued.ApiKey.Id}", issued);
            }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [exception.Message] }); }
            catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
        }).RequireManagementCsrf();

        var keys = endpoints.MapGroup("/management/v1/api-keys");
        keys.MapPatch("/{apiKeyId:guid}", async (Guid apiKeyId, UpdateApiKeyRequest request, HttpContext context, IApiKeyService service, CancellationToken cancellationToken) =>
        {
            if (!TryGetAccountId(context.User, out var accountId)) return Results.Unauthorized();
            try { return await service.SetStatusAsync(accountId, apiKeyId, request.Status, cancellationToken) ? Results.NoContent() : Results.NotFound(); }
            catch (TenantAccessDeniedException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        }).RequireManagementCsrf();

        return endpoints;
    }

    private static bool TryGetAccountId(ClaimsPrincipal user, out Guid accountId) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

    private sealed record CreateApiKeyRequest(string Name, DateTimeOffset? ExpiresAt);

    private sealed record UpdateApiKeyRequest(GatewayApiKeyStatus Status);
}
