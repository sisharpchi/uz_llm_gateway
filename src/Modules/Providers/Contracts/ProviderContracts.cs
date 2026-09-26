using System.Text.Json;

namespace UZLLM.Modules.Providers.Contracts;

public enum ProviderContentKind { Text, ImageUrl }
public sealed record ProviderContentPart(ProviderContentKind Kind, string Value);

public sealed record ProviderToolCall(string Id, string Name, string ArgumentsJson);
public sealed record ProviderMessage(string Role, IReadOnlyList<ProviderContentPart> Content,
    IReadOnlyList<ProviderToolCall>? ToolCalls = null, string? ToolCallId = null);
public sealed record ProviderToolDefinition(string Name, string? Description, JsonElement Parameters);

public enum ProviderToolChoiceMode { Auto, None, Required, Named }
public sealed record ProviderToolChoice(ProviderToolChoiceMode Mode, string? Name = null);

public enum ProviderResponseFormatKind { Text, JsonObject, JsonSchema }
public sealed record ProviderResponseFormat(ProviderResponseFormatKind Kind,
    string? SchemaName = null, JsonElement? Schema = null, bool Strict = true);

public sealed record ProviderChatRequest(
    IReadOnlyList<ProviderMessage> Messages, decimal? Temperature = null, decimal? TopP = null,
    int? MaxOutputTokens = null, IReadOnlyList<string>? Stop = null,
    IReadOnlyList<ProviderToolDefinition>? Tools = null, ProviderToolChoice? ToolChoice = null,
    ProviderResponseFormat? ResponseFormat = null);

public sealed record ProviderExecutionContext(Guid RequestId, Guid ProviderId,
    Guid ProviderModelId, Guid CredentialId, string UpstreamModelCode,
    TimeSpan Timeout, Guid? OrganizationId = null, Guid? ProjectId = null);

public sealed record ProviderUsage(int InputTokens, int OutputTokens,
    int CachedInputTokens, int? ReasoningTokens);

public sealed record ProviderCompletion(string Id, string Model, ProviderMessage Message,
    string? FinishReason, ProviderUsage? Usage, string? ProviderRequestId,
    string? Refusal = null);

public enum ProviderStreamKind { TextDelta, ToolCallDelta, Finish, Usage, Refusal, Error }
public sealed record ProviderStreamEvent(ProviderStreamKind Kind,
    string? Text = null, int? ToolIndex = null, string? ToolCallId = null,
    string? ToolName = null, string? ArgumentsDelta = null,
    string? FinishReason = null, ProviderUsage? Usage = null, ProviderError? Error = null,
    string? ProviderRequestId = null);

public enum ProviderErrorCategory
{
    Authentication, RateLimited, QuotaExhausted, InvalidRequest,
    ContextExceeded, ContentRejected, Capacity, Timeout, Upstream5xx, Unknown
}

public enum ProviderExecutionCertainty { NotDispatched, RejectedBeforeExecution, Unknown }

public sealed record ProviderError(ProviderErrorCategory Category,
    ProviderExecutionCertainty Certainty, bool Retryable, bool FallbackEligible,
    int? ProviderStatusCode, string? ProviderRequestId, string SafeMessage);

public sealed class ProviderExecutionException(ProviderError error) : Exception(error.SafeMessage)
{
    public ProviderError Error { get; } = error;
}

public interface ILlmProviderAdapter
{
    string ProviderCode { get; }
    Task<ProviderCompletion> CompleteAsync(ProviderChatRequest request,
        ProviderExecutionContext context, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ProviderStreamEvent> StreamAsync(ProviderChatRequest request,
        ProviderExecutionContext context, CancellationToken cancellationToken = default);
}

public enum ProviderCredentialStatus { Active, Disabled }

public sealed record ProviderCredential(Guid Id, Guid ProviderId, ProviderCredentialStatus Status,
    DateTimeOffset CreatedAt);

public sealed record ProtectedProviderSecret(byte[] EncryptedSecret, byte[] WrappedDataKey,
    string KeyVersion);

public sealed record StoredProviderCredential(ProviderCredential Credential,
    ProtectedProviderSecret ProtectedSecret);

public interface IProviderSecretProtector
{
    ProtectedProviderSecret Protect(Guid credentialId, Guid providerId, string secret);
    string Unprotect(Guid credentialId, Guid providerId, ProtectedProviderSecret protectedSecret);
    ProtectedProviderSecret ProtectForOrganization(Guid organizationId, Guid credentialId,
        Guid providerId, string secret);
    string UnprotectForOrganization(Guid organizationId, Guid credentialId,
        Guid providerId, ProtectedProviderSecret protectedSecret);
}

public sealed record ByokCredential(Guid Id, Guid OrganizationId, Guid ProviderId,
    string ProviderCode, string Name, string MaskedKey, ProviderCredentialStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? LastTestedAt,
    string? LastTestStatus, IReadOnlyList<Guid> ProjectIds,
    IReadOnlyList<string>? AllowedModels = null, long? SpendLimitMicroUsd = null,
    long ExternalSpentMicroUsd = 0, long ExternalReservedMicroUsd = 0);

public sealed record StoredByokCredential(ByokCredential Credential,
    ProtectedProviderSecret ProtectedSecret);

public enum ByokTestStatus { Valid, Invalid, Unavailable }

public interface IByokCredentialVerifier
{
    Task<ByokTestStatus> VerifyAsync(string providerCode, string secret,
        CancellationToken cancellationToken = default);
}

public interface IByokCredentialStore
{
    Task<bool> LockOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<string?> GetActiveProviderCodeAsync(Guid providerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ByokCredential>> ListAsync(Guid organizationId,
        CancellationToken cancellationToken = default);
    Task<StoredByokCredential?> FindAsync(Guid organizationId, Guid credentialId,
        CancellationToken cancellationToken = default);
    Task CreateAsync(StoredByokCredential credential, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Guid organizationId, Guid credentialId, string? name,
        string? maskedKey, ProtectedProviderSecret? protectedSecret, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
    Task<bool> SetStatusAsync(Guid organizationId, Guid credentialId,
        ProviderCredentialStatus status, bool deleted, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
    Task<bool> SetProjectGrantAsync(Guid organizationId, Guid credentialId, Guid projectId,
        bool enabled, DateTimeOffset createdAt, CancellationToken cancellationToken = default);
    Task<bool> SetTestResultAsync(Guid organizationId, Guid credentialId,
        ByokTestStatus result, DateTimeOffset testedAt, CancellationToken cancellationToken = default);
    Task<bool> SetRestrictionsAsync(Guid organizationId, Guid credentialId,
        IReadOnlyList<string>? allowedModels, long? spendLimitMicroUsd,
        DateTimeOffset updatedAt, CancellationToken cancellationToken = default);
    Task<ProtectedProviderSecret?> FindGrantedSecretAsync(Guid organizationId, Guid projectId,
        Guid credentialId, Guid providerId, CancellationToken cancellationToken = default);
}

public interface IByokCredentialService
{
    Task<IReadOnlyList<ByokCredential>> ListAsync(Guid actorId, Guid organizationId,
        CancellationToken cancellationToken = default);
    Task<ByokCredential?> FindAsync(Guid actorId, Guid organizationId, Guid credentialId,
        CancellationToken cancellationToken = default);
    Task<ByokCredential> CreateAsync(Guid actorId, Guid organizationId, Guid providerId,
        string name, string secret, CancellationToken cancellationToken = default);
    Task<ByokCredential?> UpdateAsync(Guid actorId, Guid organizationId, Guid credentialId,
        string? name, string? secret, CancellationToken cancellationToken = default);
    Task<bool> DisableAsync(Guid actorId, Guid organizationId, Guid credentialId,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid actorId, Guid organizationId, Guid credentialId,
        CancellationToken cancellationToken = default);
    Task<bool> SetProjectGrantAsync(Guid actorId, Guid organizationId, Guid credentialId,
        Guid projectId, bool enabled, CancellationToken cancellationToken = default);
    Task<ByokTestStatus?> TestAsync(Guid actorId, Guid organizationId, Guid credentialId,
        CancellationToken cancellationToken = default);
    Task<ByokCredential?> SetRestrictionsAsync(Guid actorId, Guid organizationId,
        Guid credentialId, IReadOnlyList<string>? allowedModels, long? spendLimitMicroUsd,
        CancellationToken cancellationToken = default);
}

public interface IByokCredentialResolver
{
    Task<string?> ResolveGrantedSecretAsync(Guid organizationId, Guid projectId,
        Guid credentialId, Guid providerId, CancellationToken cancellationToken = default);
}

public interface IProviderCredentialStore
{
    Task CreatePlatformAsync(StoredProviderCredential credential, CancellationToken cancellationToken = default);
    Task<StoredProviderCredential?> FindActivePlatformAsync(Guid credentialId, Guid providerId,
        CancellationToken cancellationToken = default);
    Task<bool> SetStatusAsync(Guid credentialId, ProviderCredentialStatus status,
        CancellationToken cancellationToken = default);
}

public interface IPlatformCredentialService
{
    Task<ProviderCredential> CreateAsync(Guid providerId, string secret,
        CancellationToken cancellationToken = default);
    Task<bool> SetStatusAsync(Guid credentialId, ProviderCredentialStatus status,
        CancellationToken cancellationToken = default);
}

public interface IProviderCredentialResolver
{
    Task<string?> ResolvePlatformSecretAsync(Guid credentialId, Guid providerId,
        CancellationToken cancellationToken = default);
    Task<string?> ResolveSecretAsync(ProviderExecutionContext context,
        CancellationToken cancellationToken = default) => context.OrganizationId is null
            && context.ProjectId is null
            ? ResolvePlatformSecretAsync(context.CredentialId, context.ProviderId, cancellationToken)
            : Task.FromResult<string?>(null);
}
