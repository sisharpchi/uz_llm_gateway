using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Anthropic;

public sealed record AnthropicAdapterOptions(Uri BaseAddress)
{
    public static AnthropicAdapterOptions Production { get; } = new(new Uri("https://api.anthropic.com/v1/"));
}

public sealed class AnthropicMessagesAdapter(HttpClient client,
    IProviderCredentialResolver credentials, AnthropicAdapterOptions options) : ILlmProviderAdapter
{
    private const string ApiVersion = "2023-06-01";
    public string ProviderCode => "anthropic";

    public async Task<ProviderCompletion> CompleteAsync(ProviderChatRequest request,
        ProviderExecutionContext context, CancellationToken cancellationToken = default)
    {
        Validate(context);
        var body = BuildRequest(request, context.UpstreamModelCode, false);
        var secret = await ResolveSecretAsync(context, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.Timeout);
        try
        {
            using var message = NewMessage(body, secret, context.RequestId);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var requestId = ResponseRequestId(response);
            if (!response.IsSuccessStatusCode)
                throw new ProviderExecutionException(AnthropicErrorClassifier.FromHttp((int)response.StatusCode,
                    await ReadErrorBodyAsync(response, timeout.Token), requestId));
            await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token));
            return AnthropicWireMapper.ParseCompletion(document.RootElement, requestId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderExecutionException(AnthropicErrorClassifier.TransportTimeout()); }
        catch (HttpRequestException)
        { throw new ProviderExecutionException(AnthropicErrorClassifier.Unknown(null)); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
            or OverflowException)
        { throw new ProviderExecutionException(AnthropicErrorClassifier.Unknown(null)); }
    }

    public async IAsyncEnumerable<ProviderStreamEvent> StreamAsync(ProviderChatRequest request,
        ProviderExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Validate(context);
        var body = BuildRequest(request, context.UpstreamModelCode, true);
        var secret = await ResolveSecretAsync(context, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.Timeout);
        using var message = NewMessage(body, secret, context.RequestId);
        HttpResponseMessage response;
        try { response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderExecutionException(AnthropicErrorClassifier.TransportTimeout()); }
        catch (HttpRequestException)
        { throw new ProviderExecutionException(AnthropicErrorClassifier.Unknown(null)); }
        using (response)
        {
            var requestId = ResponseRequestId(response);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadErrorBodyAsync(response, timeout.Token);
                throw new ProviderExecutionException(AnthropicErrorClassifier.FromHttp((int)response.StatusCode,
                    errorBody, requestId));
            }
            Stream stream;
            try { stream = await response.Content.ReadAsStreamAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new ProviderExecutionException(AnthropicErrorClassifier.TransportTimeout()); }

            var parser = new AnthropicStreamParser(requestId);
            await using var frames = AnthropicSseReader.ReadAsync(stream, timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            while (true)
            {
                bool hasFrame;
                try { hasFrame = await frames.MoveNextAsync(); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new ProviderExecutionException(AnthropicErrorClassifier.TransportTimeout()); }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                { throw new ProviderExecutionException(AnthropicErrorClassifier.Unknown(requestId)); }
                if (!hasFrame) break;
                IReadOnlyList<ProviderStreamEvent> events;
                try { events = parser.Parse(frames.Current); }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException
                    or OverflowException)
                { throw new ProviderExecutionException(AnthropicErrorClassifier.Unknown(requestId)); }
                foreach (var item in events) yield return item;
                if (parser.Completed) break;
            }
            if (!parser.Completed)
                throw new ProviderExecutionException(AnthropicErrorClassifier.Unknown(requestId));
        }
    }

    private static byte[] BuildRequest(ProviderChatRequest request, string model, bool stream)
    {
        try { return AnthropicWireMapper.BuildRequest(request, model, stream); }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        { throw new ProviderExecutionException(new ProviderError(ProviderErrorCategory.InvalidRequest,
            ProviderExecutionCertainty.NotDispatched, false, false, null, null,
            "Request is unsupported by this provider.")); }
    }

    private async Task<string> ResolveSecretAsync(ProviderExecutionContext context,
        CancellationToken cancellationToken)
    {
        var secret = await credentials.ResolvePlatformSecretAsync(context.CredentialId,
            context.ProviderId, cancellationToken);
        return !string.IsNullOrWhiteSpace(secret) ? secret
            : throw new ProviderExecutionException(new ProviderError(
                ProviderErrorCategory.Authentication, ProviderExecutionCertainty.NotDispatched,
                false, false, null, null, "Provider credential is unavailable."));
    }

    private static HttpRequestMessage NewMessage(byte[] body, string secret, Guid requestId)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = new ByteArrayContent(body)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.TryAddWithoutValidation("x-api-key", secret);
        message.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        message.Headers.TryAddWithoutValidation("X-Client-Request-Id", requestId.ToString("N"));
        return message;
    }

    private static string? ResponseRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("request-id", out var values) ? values.FirstOrDefault() : null;

    private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[16 * 1024];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        return Encoding.UTF8.GetString(buffer, 0, count);
    }

    private void Validate(ProviderExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.RequestId == Guid.Empty || context.ProviderId == Guid.Empty
            || context.ProviderModelId == Guid.Empty || context.CredentialId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.UpstreamModelCode) || context.Timeout <= TimeSpan.Zero
            || options.BaseAddress.Scheme != Uri.UriSchemeHttps
            || !options.BaseAddress.AbsolutePath.EndsWith("/v1/", StringComparison.Ordinal)
            || options.BaseAddress.UserInfo.Length != 0 || options.BaseAddress.Query.Length != 0)
            throw new ArgumentException("Valid Anthropic execution context and HTTPS v1 endpoint are required.");
    }
}
