namespace UZLLM.Modules.Usage.Contracts;

public sealed record PayloadRetentionPolicy(bool Enabled, int RetentionMinutes);

public sealed record RetainedPayload(Guid RequestId, Guid ProjectId, string Request, string? Response,
    DateTimeOffset ExpiresAt);

public interface IPayloadRetentionService
{
    Task<PayloadRetentionPolicy> GetPolicyAsync(Guid organizationId, Guid projectId,
        CancellationToken cancellationToken);
    Task<PayloadRetentionPolicy?> SetPolicyAsync(Guid organizationId, Guid projectId,
        Guid accountId, bool enabled, int retentionMinutes, CancellationToken cancellationToken);
    Task<bool> CaptureRequestAsync(Guid organizationId, Guid projectId, Guid requestId,
        byte[] payload, CancellationToken cancellationToken);
    Task CaptureResponseAsync(Guid organizationId, Guid projectId, Guid requestId,
        byte[] payload, CancellationToken cancellationToken);
    Task<RetainedPayload?> GetPayloadAsync(Guid organizationId, Guid projectId,
        Guid requestId, CancellationToken cancellationToken);
    Task<int> DeleteExpiredAsync(int batchSize, CancellationToken cancellationToken);
}
