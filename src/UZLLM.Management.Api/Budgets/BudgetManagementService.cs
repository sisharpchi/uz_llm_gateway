using UZLLM.Modules.ApiKeys.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Billing.Domain;
using UZLLM.Modules.Organizations.Contracts;
using UZLLM.Modules.Projects.Contracts;

namespace UZLLM.Management.Api.Budgets;

public interface IBudgetManagementService
{
    Task<IReadOnlyList<BudgetPolicy>> ListAsync(Guid accountId, Guid projectId,
        CancellationToken cancellationToken = default);

    Task<BudgetPolicy?> SetAsync(Guid accountId, Guid projectId, Guid? apiKeyId,
        BudgetPeriod period, UsdMicroAmount limit, CancellationToken cancellationToken = default);
}

public sealed class BudgetManagementService(IProjectAccessService projects, IApiKeyStore keys,
    IFinancialService financial) : IBudgetManagementService
{
    public async Task<IReadOnlyList<BudgetPolicy>> ListAsync(Guid accountId, Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetAuthorizedAsync(accountId, projectId,
            OrganizationPermission.ReadBilling, cancellationToken)
            ?? throw new KeyNotFoundException("Project not found.");
        return await financial.ListBudgetsAsync(project.OrganizationId, projectId, cancellationToken);
    }

    public async Task<BudgetPolicy?> SetAsync(Guid accountId, Guid projectId, Guid? apiKeyId,
        BudgetPeriod period, UsdMicroAmount limit, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetAuthorizedAsync(accountId, projectId,
            OrganizationPermission.ManageBilling, cancellationToken)
            ?? throw new KeyNotFoundException("Project not found.");
        if (apiKeyId is { } keyId)
        {
            var key = await keys.FindByIdAsync(keyId, cancellationToken);
            if (key?.ProjectId != projectId) throw new KeyNotFoundException("API key not found.");
        }
        return await financial.SetBudgetAsync(project.OrganizationId, projectId, apiKeyId,
            period, limit, cancellationToken);
    }
}
