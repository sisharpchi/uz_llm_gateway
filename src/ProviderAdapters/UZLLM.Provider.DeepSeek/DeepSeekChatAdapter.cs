using System.Net.Http.Headers;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.DeepSeek;

public sealed record DeepSeekAdapterOptions(Uri BaseAddress)
{
    public static DeepSeekAdapterOptions Production { get; } = new(new Uri("https://api.deepseek.com/v1/"));
}

public sealed class DeepSeekChatAdapter(HttpClient client, IProviderCredentialResolver credentials,
    DeepSeekAdapterOptions options) : ILlmProviderAdapter
{
    public string ProviderCode => "deepseek";
    public bool Supports(ProviderChatRequest request, bool stream) => DeepSeekWireMapper.Supports(request, stream);

    public async Task<ProviderCompletion> CompleteAsync(ProviderChatRequest request,
        ProviderExecutionContext context, CancellationToken cancellationToken = default)
    {
        Validate(context);
        byte[] body;
        try { body = DeepSeekWireMapper.BuildRequest(request, context.UpstreamModelCode,
            request.MaxOutputTokens ?? 1024); }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        { throw InvalidRequest(); }

        var secret = await credentials.ResolveSecretAsync(context, cancellationToken);
        if (string.IsNullOrWhiteSpace(secret))
            throw new ProviderExecutionException(new ProviderError(ProviderErrorCategory.Authentication,
                ProviderExecutionCertainty.NotDispatched, false, false, null, null,
                "Provider credential is unavailable."));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.Timeout);
        string? requestId = null;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            { Content = new ByteArrayContent(body) };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            message.Headers.TryAddWithoutValidation("X-Client-Request-Id", context.RequestId.ToString("N"));
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            requestId = response.Headers.TryGetValues("x-request-id", out var values)
                ? values.FirstOrDefault() : null;
            if (!response.IsSuccessStatusCode)
                throw new ProviderExecutionException(DeepSeekErrorClassifier.FromHttp(
                    (int)response.StatusCode, requestId));
            await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String)
                requestId = id.GetString();
            return DeepSeekWireMapper.ParseCompletion(document.RootElement,
                context.UpstreamModelCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderExecutionException(DeepSeekErrorClassifier.Timeout(requestId)); }
        catch (HttpRequestException)
        { throw new ProviderExecutionException(DeepSeekErrorClassifier.Unknown(requestId)); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or OverflowException or FormatException)
        { throw new ProviderExecutionException(DeepSeekErrorClassifier.Unknown(requestId)); }
    }

    public IAsyncEnumerable<ProviderStreamEvent> StreamAsync(ProviderChatRequest request,
        ProviderExecutionContext context, CancellationToken cancellationToken = default) =>
        throw InvalidRequest();

    private static ProviderExecutionException InvalidRequest() => new(new ProviderError(
        ProviderErrorCategory.InvalidRequest, ProviderExecutionCertainty.NotDispatched, false, false,
        null, null, "Request is unsupported by this provider."));

    private void Validate(ProviderExecutionContext context)
    {
        if (context is null || context.RequestId == Guid.Empty || context.ProviderId == Guid.Empty
            || context.ProviderModelId == Guid.Empty || context.CredentialId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.UpstreamModelCode)
            || context.UpstreamModelCode.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_' and not '.')
            || context.Timeout <= TimeSpan.Zero
            || options.BaseAddress.Scheme != Uri.UriSchemeHttps
            || options.BaseAddress.AbsolutePath != "/v1/"
            || options.BaseAddress.UserInfo.Length != 0 || options.BaseAddress.Query.Length != 0
            || options.BaseAddress.Fragment.Length != 0)
            throw InvalidRequest();
    }
}
