using System.Text.Json;
using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.ApiKeys.Domain;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Projects.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Modules.ApiKeys.Application;

public sealed class TenantLimitService(ITenantLimitStore store, IApiKeyStore keys,
    IProjectAccessService projects, TenantLimitCeilings ceilings, IAuditTrail audit,
    ITransactionCoordinator transactions) : ITenantLimitService
{
    public async Task<TenantLimitView> ReadAsync(Guid accountId, Guid resourceId, bool apiKey,
        CancellationToken cancellationToken = default)
    {
        var project = await AuthorizeAsync(accountId, resourceId, apiKey, false, cancellationToken);
        var value = await store.ReadAsync(project.Id, apiKey ? resourceId : null, cancellationToken)
            ?? throw new KeyNotFoundException();
        return View(value, apiKey);
    }

    public async Task<TenantLimitView> ReplaceAsync(Guid accountId, Guid resourceId, bool apiKey, TenantLimits limits,
        CancellationToken cancellationToken = default)
    {
        var project = await AuthorizeAsync(accountId, resourceId, apiKey, true, cancellationToken);
        TenantLimitPolicy.Validate(limits, apiKey ? ceilings.ApiKey : ceilings.Project);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (!await store.ReplaceAsync(project.Id, apiKey ? resourceId : null, limits, cancellationToken))
            throw new KeyNotFoundException();
        await audit.RecordAsync(new(project.OrganizationId, accountId,
            apiKey ? "api_key.limits_changed" : "project.limits_changed",
            apiKey ? "api_key" : "project", resourceId, null, JsonSerializer.Serialize(limits)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return View(limits, apiKey);
    }

    private async Task<Project> AuthorizeAsync(Guid accountId, Guid id, bool apiKey, bool write, CancellationToken ct)
    {
        var projectId = apiKey ? (await keys.FindByIdAsync(id, ct)
            ?? throw new KeyNotFoundException()).ProjectId : id;
        return await projects.GetAuthorizedAsync(accountId, projectId, write
            ? apiKey ? OrganizationPermission.ManageApiKeys : OrganizationPermission.ManageProjects
            : OrganizationPermission.ReadProjects, ct) ?? throw new KeyNotFoundException();
    }

    private TenantLimitView View(TenantLimits limits, bool apiKey)
    {
        var ceiling = apiKey ? ceilings.ApiKey : ceilings.Project;
        return new(limits, TenantLimitPolicy.Effective(limits, ceiling), ceiling);
    }
}
