using System.Net.Http.Headers;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Provider.OpenAI;

public sealed partial class OpenAiChatAdapter
{
    public async Task<ProviderEmbeddingResult> EmbedAsync(ProviderEmbeddingRequest request,
        ProviderExecutionContext context, CancellationToken cancellationToken = default)
    {
        Validate(context, options);
        if (request.Inputs.Count is < 1 or > 32 || request.Inputs.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("One to 32 nonempty text inputs are required.", nameof(request));
        var secret = await ResolveSecretAsync(context, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.Timeout);
        string? requestId = null;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "embeddings")
            {
                Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new
                { model = context.UpstreamModelCode, input = request.Inputs, encoding_format = "float" }))
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            message.Headers.TryAddWithoutValidation("X-Client-Request-Id", context.RequestId.ToString("N"));
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            requestId = ResponseRequestId(response);
            if (!response.IsSuccessStatusCode)
                throw new ProviderExecutionException(OpenAiErrorClassifier.FromHttp((int)response.StatusCode,
                    await ReadErrorBodyAsync(response, timeout.Token), requestId));
            await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token));
            var root = document.RootElement;
            if (root.EnumerateObject().Select(p => p.Name).Distinct().Count() != root.EnumerateObject().Count()
                || root.GetProperty("object").GetString() != "list"
                || root.GetProperty("model").GetString() != context.UpstreamModelCode)
                throw new JsonException("Invalid embedding envelope.");
            var data = root.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != request.Inputs.Count)
                throw new JsonException("Embedding cardinality mismatch.");
            var vectors = new List<ProviderEmbedding>();
            int? dimensions = null;
            foreach (var item in data.EnumerateArray())
            {
                if (item.GetProperty("object").GetString() != "embedding"
                    || item.EnumerateObject().Select(p => p.Name).Distinct().Count() != item.EnumerateObject().Count())
                    throw new JsonException("Invalid embedding item.");
                var index = item.GetProperty("index").GetInt32();
                var vector = item.GetProperty("embedding");
                if (index < 0 || index >= request.Inputs.Count || vectors.Any(v => v.Index == index)
                    || vector.ValueKind != JsonValueKind.Array || vector.GetArrayLength() is < 1 or > 4096
                    || dimensions is not null && vector.GetArrayLength() != dimensions)
                    throw new JsonException("Invalid embedding shape.");
                var values = vector.EnumerateArray().Select(value => value.GetSingle()).ToArray();
                if (values.Any(value => !float.IsFinite(value))) throw new JsonException("Invalid vector value.");
                dimensions = values.Length;
                vectors.Add(new ProviderEmbedding(index, values));
            }
            ProviderUsage? usage = null;
            if (root.TryGetProperty("usage", out var u) && u.ValueKind != JsonValueKind.Null)
            {
                // Invalid/contradictory counters are unknown evidence, never a zero charge.
                if (u.ValueKind == JsonValueKind.Object
                    && u.EnumerateObject().Select(p => p.Name).Distinct().Count() == u.EnumerateObject().Count()
                    && u.TryGetProperty("prompt_tokens", out var prompt) && prompt.ValueKind == JsonValueKind.Number && prompt.TryGetInt32(out var input)
                    && u.TryGetProperty("total_tokens", out var total) && total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out var count)
                    && input > 0 && input == count)
                    usage = new ProviderUsage(input, 0, 0, null);
            }
            return new ProviderEmbeddingResult(vectors.OrderBy(v => v.Index).ToArray(), usage, requestId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ProviderExecutionException(OpenAiErrorClassifier.TransportTimeout() with { ProviderRequestId = requestId }); }
        catch (HttpRequestException)
        { throw new ProviderExecutionException(OpenAiErrorClassifier.TransportFailure() with { ProviderRequestId = requestId }); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or IOException)
        { throw new ProviderExecutionException(OpenAiErrorClassifier.UnknownStream(requestId)); }
    }
}
