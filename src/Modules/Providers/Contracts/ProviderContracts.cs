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
    TimeSpan Timeout);

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
}
