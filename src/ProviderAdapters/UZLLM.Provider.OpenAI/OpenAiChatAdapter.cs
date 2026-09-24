using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.OpenAI;

public sealed record OpenAiAdapterOptions(Uri BaseAddress)
{
    public static OpenAiAdapterOptions Production { get; } = new(new Uri("https://api.openai.com/v1/"));
}

public sealed class OpenAiChatAdapter(HttpClient client,
    IProviderCredentialResolver credentials, OpenAiAdapterOptions options) : ILlmProviderAdapter
{
    public string ProviderCode => "openai";

    public async Task<ProviderCompletion> CompleteAsync(ProviderChatRequest request,
        ProviderExecutionContext context, CancellationToken cancellationToken = default)
    {
        Validate(context, options);
        var body = OpenAiWireMapper.BuildRequest(request, context.UpstreamModelCode, false);
        var secret = await ResolveSecretAsync(context, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.Timeout);
        try
        {
            using var message = NewMessage(body, secret, context.RequestId);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var providerRequestId = ResponseRequestId(response);
            if (!response.IsSuccessStatusCode)
                throw new ProviderExecutionException(OpenAiErrorClassifier.FromHttp((int)response.StatusCode,
                    await ReadErrorBodyAsync(response, timeout.Token), providerRequestId));
            await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token));
            return OpenAiWireMapper.ParseCompletion(document.RootElement, providerRequestId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderExecutionException(OpenAiErrorClassifier.TransportTimeout()); }
        catch (HttpRequestException)
        { throw new ProviderExecutionException(OpenAiErrorClassifier.TransportFailure()); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new ProviderExecutionException(OpenAiErrorClassifier.UnknownStream(null)); }
    }

    public async IAsyncEnumerable<ProviderStreamEvent> StreamAsync(ProviderChatRequest request,
        ProviderExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Validate(context, options);
        var body = OpenAiWireMapper.BuildRequest(request, context.UpstreamModelCode, true);
        var secret = await ResolveSecretAsync(context, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.Timeout);
        using var message = NewMessage(body, secret, context.RequestId);
        HttpResponseMessage response;
        try { response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderExecutionException(OpenAiErrorClassifier.TransportTimeout()); }
        catch (HttpRequestException)
        { throw new ProviderExecutionException(OpenAiErrorClassifier.TransportFailure()); }
        using (response)
        {
            var providerRequestId = ResponseRequestId(response);
            if (!response.IsSuccessStatusCode)
            {
                string errorBody;
                try { errorBody = await ReadErrorBodyAsync(response, timeout.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new ProviderExecutionException(OpenAiErrorClassifier.TransportTimeout()); }
                throw new ProviderExecutionException(OpenAiErrorClassifier.FromHttp((int)response.StatusCode,
                    errorBody, providerRequestId));
            }

            Stream stream;
            try { stream = await response.Content.ReadAsStreamAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new ProviderExecutionException(OpenAiErrorClassifier.TransportTimeout()); }

            await using var frames = OpenAiSseReader.ReadAsync(stream, timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            var completed = false;
            while (true)
            {
                bool hasFrame;
                try { hasFrame = await frames.MoveNextAsync(); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new ProviderExecutionException(OpenAiErrorClassifier.TransportTimeout()); }
                catch (InvalidDataException)
                { throw new ProviderExecutionException(OpenAiErrorClassifier.UnknownStream(providerRequestId)); }
                catch (IOException)
                { throw new ProviderExecutionException(OpenAiErrorClassifier.UnknownStream(providerRequestId)); }
                if (!hasFrame) break;
                var events = ParseFrame(frames.Current, providerRequestId, out var done);
                foreach (var item in events) yield return item;
                if (done) { completed = true; break; }
            }
            if (!completed)
                throw new ProviderExecutionException(OpenAiErrorClassifier.UnknownStream(providerRequestId));
        }
    }

    private static IReadOnlyList<ProviderStreamEvent> ParseFrame(OpenAiSseFrame frame,
        string? providerRequestId, out bool done)
    {
        done = false;
        if (frame.Data == "[DONE]") { done = true; return []; }
        if (frame.EventType == "error")
        {
            done = true;
            return [new ProviderStreamEvent(ProviderStreamKind.Error,
                Error: OpenAiErrorClassifier.UnknownStream(providerRequestId),
                ProviderRequestId: providerRequestId)];
        }
        try
        {
            using var document = JsonDocument.Parse(frame.Data);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out _))
            {
                done = true;
                return [new ProviderStreamEvent(ProviderStreamKind.Error,
                    Error: OpenAiErrorClassifier.UnknownStream(providerRequestId),
                    ProviderRequestId: providerRequestId)];
            }
            var events = new List<ProviderStreamEvent>();
            var usage = OpenAiWireMapper.ParseUsage(root);
            if (usage is not null)
                events.Add(new ProviderStreamEvent(ProviderStreamKind.Usage,
                    Usage: usage, ProviderRequestId: providerRequestId));
            foreach (var choice in root.GetProperty("choices").EnumerateArray())
            {
                if (choice.GetProperty("index").GetInt32() != 0)
                    throw new JsonException("Only one OpenAI choice is supported.");
                var delta = choice.GetProperty("delta");
                var text = OpenAiWireMapper.GetOptionalString(delta, "content");
                if (!string.IsNullOrEmpty(text))
                    events.Add(new ProviderStreamEvent(ProviderStreamKind.TextDelta,
                        Text: text, ProviderRequestId: providerRequestId));
                var refusal = OpenAiWireMapper.GetOptionalString(delta, "refusal");
                if (!string.IsNullOrEmpty(refusal))
                    events.Add(new ProviderStreamEvent(ProviderStreamKind.Refusal,
                        Text: refusal, ProviderRequestId: providerRequestId));
                if (delta.TryGetProperty("tool_calls", out var toolCalls)
                    && toolCalls.ValueKind == JsonValueKind.Array)
                    foreach (var tool in toolCalls.EnumerateArray())
                    {
                        var function = tool.TryGetProperty("function", out var fn) ? fn : default;
                        events.Add(new ProviderStreamEvent(ProviderStreamKind.ToolCallDelta,
                            ToolIndex: tool.GetProperty("index").GetInt32(),
                            ToolCallId: OpenAiWireMapper.GetOptionalString(tool, "id"),
                            ToolName: function.ValueKind == JsonValueKind.Object
                                ? OpenAiWireMapper.GetOptionalString(function, "name") : null,
                            ArgumentsDelta: function.ValueKind == JsonValueKind.Object
                                ? OpenAiWireMapper.GetOptionalString(function, "arguments") : null,
                            ProviderRequestId: providerRequestId));
                    }
                var finish = OpenAiWireMapper.GetOptionalString(choice, "finish_reason");
                if (finish is not null)
                    events.Add(new ProviderStreamEvent(ProviderStreamKind.Finish,
                        FinishReason: finish, ProviderRequestId: providerRequestId));
            }
            return events;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new ProviderExecutionException(OpenAiErrorClassifier.UnknownStream(providerRequestId)); }
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
        var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new ByteArrayContent(body)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        message.Headers.TryAddWithoutValidation("X-Client-Request-Id", requestId.ToString("N"));
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

    private static void Validate(ProviderExecutionContext context, OpenAiAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        if (context.RequestId == Guid.Empty || context.ProviderId == Guid.Empty
            || context.ProviderModelId == Guid.Empty || context.CredentialId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.UpstreamModelCode) || context.Timeout <= TimeSpan.Zero
            || options.BaseAddress.Scheme != Uri.UriSchemeHttps
            || !options.BaseAddress.AbsolutePath.EndsWith("/v1/", StringComparison.Ordinal)
            || options.BaseAddress.UserInfo.Length != 0 || options.BaseAddress.Query.Length != 0)
            throw new ArgumentException("Valid OpenAI execution context and HTTPS v1 endpoint are required.");
    }
}
