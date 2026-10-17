using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using UZLLM.Modules.Audit.Contracts;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Persistence;

namespace UZLLM.Management.Api.Administration;

/// <summary>Request a positive USD credit; does not mutate the wallet until independently approved.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateWalletAdjustmentRequest(Guid OrganizationId, string AdjustmentKey, string AmountMicroUsd, string Reason);

/// <summary>Approve exactly the immutable request terms; amount/organization overrides are forbidden.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveWalletAdjustmentRequest(string Reason);

/// <summary>Immutable maker/checker evidence; amounts are exact USD micro-unit strings.</summary>
public sealed record AdminWalletAdjustmentResponse(Guid Id, Guid OrganizationId, Guid RequestedByAccountId,
    string AdjustmentKey, string AmountMicroUsd, string Reason, DateTimeOffset CreatedAt,
    string Status, AdminWalletAdjustmentApprovalResponse? Approval, bool Duplicate);

/// <summary>Single independently authorized approval; never a cash payout.</summary>
public sealed record AdminWalletAdjustmentApprovalResponse(Guid Id, Guid ApprovedByAccountId, string Reason, DateTimeOffset CreatedAt);

public interface IWalletAdjustmentAdminService
{
    Task<AdminWalletAdjustmentResponse> RequestAsync(Guid actorId, CreateWalletAdjustmentRequest request, CancellationToken cancellationToken);
    Task<AdminWalletAdjustmentResponse> ApproveAsync(Guid actorId, Guid requestId, ApproveWalletAdjustmentRequest request, CancellationToken cancellationToken);
    Task<AdminWalletAdjustmentResponse?> FindAsync(Guid requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminWalletAdjustmentResponse>> ListAsync(Guid? organizationId, int limit, CancellationToken cancellationToken);
}

// Same committed-state + audit boundary as settlement refunds; finance invariants remain in Billing.
public sealed class WalletAdjustmentAdminService(IWalletAdjustmentStore store, IAuditTrail audit,
    ITransactionCoordinator transactions, TimeProvider clock) : IWalletAdjustmentAdminService
{
    public async Task<AdminWalletAdjustmentResponse> RequestAsync(Guid actorId, CreateWalletAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!long.TryParse(request.AmountMicroUsd, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new ArgumentException("Amount must be a positive USD micro-unit integer string.");
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var result = await store.RequestAsync(actorId, request.OrganizationId, request.AdjustmentKey,
            new UsdMicroAmount(amount), request.Reason, clock.GetUtcNow(), cancellationToken);
        if (!result.Duplicate)
            await audit.RecordAsync(new AuditEventInput(result.OrganizationId, actorId,
                "wallet-adjustment.requested", "wallet_adjustment", result.Id, null,
                JsonSerializer.Serialize(new { amountMicroUsd = result.Amount.Value, reason = result.Reason })), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Response(result);
    }

    public async Task<AdminWalletAdjustmentResponse> ApproveAsync(Guid actorId, Guid requestId,
        ApproveWalletAdjustmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        var result = await store.ApproveAsync(actorId, requestId, request.Reason, clock.GetUtcNow(), cancellationToken);
        if (!result.Duplicate)
            await audit.RecordAsync(new AuditEventInput(result.OrganizationId, actorId,
                "wallet-adjustment.approved", "wallet_adjustment", result.Id, null,
                JsonSerializer.Serialize(new { approvalId = result.Approval!.Id, requestedByAccountId = result.RequestedByAccountId,
                    amountMicroUsd = result.Amount.Value, reason = result.Approval.Reason })), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Response(result);
    }

    public async Task<AdminWalletAdjustmentResponse?> FindAsync(Guid requestId, CancellationToken cancellationToken) =>
        await store.FindAsync(requestId, cancellationToken) is { } result ? Response(result) : null;

    public async Task<IReadOnlyList<AdminWalletAdjustmentResponse>> ListAsync(Guid? organizationId, int limit,
        CancellationToken cancellationToken) =>
        (await store.ListAsync(organizationId, limit, cancellationToken)).Select(Response).ToArray();

    private static AdminWalletAdjustmentResponse Response(WalletAdjustment value) => new(value.Id, value.OrganizationId,
        value.RequestedByAccountId, value.AdjustmentKey, value.Amount.Value.ToString(CultureInfo.InvariantCulture),
        value.Reason, value.CreatedAt, value.Approval is null ? "Pending" : "Approved",
        value.Approval is null ? null : new(value.Approval.Id, value.Approval.ApprovedByAccountId,
            value.Approval.Reason, value.Approval.CreatedAt), value.Duplicate);
}
