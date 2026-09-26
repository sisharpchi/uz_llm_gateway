using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.Google;

public sealed record GoogleAdapterOptions(Uri BaseAddress)
{
    public static GoogleAdapterOptions Production { get; } = new(new Uri("https://generativelanguage.googleapis.com/v1beta/"));
}

public sealed class GoogleContentAdapter(HttpClient client, IProviderCredentialResolver credentials,
    GoogleAdapterOptions options) : ILlmProviderAdapter
{
    public string ProviderCode => "google";
    public bool Supports(ProviderChatRequest request, bool stream) => GoogleWireMapper.Supports(request, stream);

    public async Task<ProviderCompletion> CompleteAsync(ProviderChatRequest request,
        ProviderExecutionContext context, CancellationToken cancellationToken = default)
    {
        Validate(context);
        byte[] body;
        try { body = GoogleWireMapper.BuildRequest(request, request.MaxOutputTokens ?? 1024); }
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
            using var message = NewMessage(body, secret, context, false);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            requestId = ResponseRequestId(response);
            if (!response.IsSuccessStatusCode)
                throw new ProviderExecutionException(GoogleErrorClassifier.FromHttp((int)response.StatusCode,
                    await ReadErrorBodyAsync(response, timeout.Token), requestId));
            await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token));
            if (requestId is null && document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("responseId", out var responseId)
                && responseId.ValueKind == JsonValueKind.String)
                requestId = responseId.GetString();
            return GoogleWireMapper.ParseCompletion(document.RootElement, requestId,
                context.UpstreamModelCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderExecutionException(GoogleErrorClassifier.Timeout(requestId)); }
        catch (HttpRequestException)
        { throw new ProviderExecutionException(GoogleErrorClassifier.Unknown(requestId)); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException
            or OverflowException or FormatException)
        { throw new ProviderExecutionException(GoogleErrorClassifier.Unknown(requestId)); }
    }

    public async IAsyncEnumerable<ProviderStreamEvent> StreamAsync(ProviderChatRequest request,
        ProviderExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Validate(context);
        byte[] body;
        try { body = GoogleWireMapper.BuildRequest(request, request.MaxOutputTokens ?? 1024); }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        { throw InvalidRequest(); }

        var secret = await credentials.ResolveSecretAsync(context, cancellationToken);
        if (string.IsNullOrWhiteSpace(secret))
            throw new ProviderExecutionException(new ProviderError(ProviderErrorCategory.Authentication,
                ProviderExecutionCertainty.NotDispatched, false, false, null, null,
                "Provider credential is unavailable."));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.Timeout);
        using var message = NewMessage(body, secret, context, true);
        HttpResponseMessage response;
        try { response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderExecutionException(GoogleErrorClassifier.Timeout(null)); }
        catch (HttpRequestException)
        { throw new ProviderExecutionException(GoogleErrorClassifier.Unknown(null)); }
        using (response)
        {
            var requestId = ResponseRequestId(response);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody;
                try { errorBody = await ReadErrorBodyAsync(response, timeout.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new ProviderExecutionException(GoogleErrorClassifier.Timeout(requestId)); }
                throw new ProviderExecutionException(GoogleErrorClassifier.FromHttp(
                    (int)response.StatusCode, errorBody, requestId));
            }
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType,
                "text/event-stream", StringComparison.OrdinalIgnoreCase))
                throw new ProviderExecutionException(GoogleErrorClassifier.Unknown(requestId));

            Stream stream;
            try { stream = await response.Content.ReadAsStreamAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new ProviderExecutionException(GoogleErrorClassifier.Timeout(requestId)); }
            catch (HttpRequestException)
            { throw new ProviderExecutionException(GoogleErrorClassifier.Unknown(requestId)); }

            var parser = new GoogleStreamParser(requestId);
            await using var frames = GoogleSseReader.ReadAsync(stream, timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            while (true)
            {
                bool hasFrame;
                try { hasFrame = await frames.MoveNextAsync(); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new ProviderExecutionException(GoogleErrorClassifier.Timeout(parser.ProviderRequestId)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                { throw new ProviderExecutionException(GoogleErrorClassifier.Unknown(parser.ProviderRequestId)); }
                if (!hasFrame) break;
                IReadOnlyList<ProviderStreamEvent> events;
                try { events = parser.Parse(frames.Current); }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException
                    or OverflowException or FormatException)
                { throw new ProviderExecutionException(GoogleErrorClassifier.Unknown(parser.ProviderRequestId)); }
                foreach (var item in events)
                {
                    yield return item;
                    if (item.Kind == ProviderStreamKind.Error) yield break;
                }
            }
            IReadOnlyList<ProviderStreamEvent> terminal;
            try { terminal = parser.Complete(); }
            catch (JsonException)
            { throw new ProviderExecutionException(GoogleErrorClassifier.Unknown(parser.ProviderRequestId)); }
            foreach (var item in terminal) yield return item;
        }
    }

    private static ProviderExecutionException InvalidRequest() => new(new ProviderError(
        ProviderErrorCategory.InvalidRequest, ProviderExecutionCertainty.NotDispatched, false, false,
        null, null, "Request is unsupported by this provider."));

    private static HttpRequestMessage NewMessage(byte[] body, string secret,
        ProviderExecutionContext context, bool stream)
    {
        var method = stream ? "streamGenerateContent?alt=sse" : "generateContent";
        var message = new HttpRequestMessage(HttpMethod.Post,
            $"models/{Uri.EscapeDataString(context.UpstreamModelCode)}:{method}")
        {
            Content = new ByteArrayContent(body)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.TryAddWithoutValidation("x-goog-api-key", secret);
        message.Headers.TryAddWithoutValidation("X-Client-Request-Id", context.RequestId.ToString("N"));
        if (stream) message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return message;
    }

    private static string? ResponseRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-request-id", out var values) ? values.FirstOrDefault() : null;

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
        if (context is null || context.RequestId == Guid.Empty || context.ProviderId == Guid.Empty
            || context.ProviderModelId == Guid.Empty || context.CredentialId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.UpstreamModelCode)
            || context.UpstreamModelCode.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_' and not '.')
            || context.Timeout <= TimeSpan.Zero
            || options.BaseAddress.Scheme != Uri.UriSchemeHttps
            || !options.BaseAddress.AbsolutePath.EndsWith("/v1beta/", StringComparison.Ordinal)
            || options.BaseAddress.UserInfo.Length != 0 || options.BaseAddress.Query.Length != 0
            || options.BaseAddress.Fragment.Length != 0)
            throw InvalidRequest();
    }
}
