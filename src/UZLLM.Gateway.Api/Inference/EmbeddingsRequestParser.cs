using System.Text;
using System.Text.Json;
using UZLLM.Modules.Providers.Contracts;

namespace UZLLM.Gateway.Api.Inference;

public static class EmbeddingsRequestParser
{
    public static ParsedChatRequest Parse(ReadOnlyMemory<byte> utf8)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8);
            var root = document.RootElement;
            Fields(root, "model", "input", "encoding_format", "uzllm");
            var model = root.GetProperty("model").GetString();
            if (string.IsNullOrWhiteSpace(model) || model.Length > 200)
                throw new GatewayRequestException("model is required (1–200 characters).");
            if (root.TryGetProperty("encoding_format", out var encoding) && encoding.GetString() != "float")
                throw new GatewayRequestException("Only float encoding is supported.", "unsupported_parameter");
            var input = root.GetProperty("input");
            var inputs = input.ValueKind == JsonValueKind.String ? [input.GetString()!]
                : input.ValueKind == JsonValueKind.Array && input.GetArrayLength() is >= 1 and <= 32
                    ? input.EnumerateArray().Select(value => value.GetString()!).ToArray()
                    : throw new GatewayRequestException("input must be text or 1–32 texts.");
            if (inputs.Any(value => string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 8192))
                throw new GatewayRequestException("Each input must contain 1–8192 UTF-8 bytes of nonempty text.");
            Guid? credential = null;
            var hybrid = false;
            string? routing = null;
            if (root.TryGetProperty("uzllm", out var extension))
            {
                Fields(extension, "provider_key_id", "allow_managed_fallback", "routing");
                if (extension.TryGetProperty("provider_key_id", out var key))
                {
                    if (!Guid.TryParse(key.GetString(), out var id) || id == Guid.Empty)
                        throw new GatewayRequestException("provider_key_id must be a nonempty UUID.");
                    credential = id;
                }
                if (extension.TryGetProperty("allow_managed_fallback", out var fallback)) hybrid = fallback.GetBoolean();
                if (hybrid && credential is null)
                    throw new GatewayRequestException("Managed fallback requires explicit provider_key_id.");
                if (extension.TryGetProperty("routing", out var strategy))
                {
                    routing = strategy.GetString();
                    if (routing is not ("priority" or "price"))
                        throw new GatewayRequestException("Embeddings supports priority or price routing.", "unsupported_parameter");
                }
            }
            // The existing admission envelope is shared, not the upstream chat operation.
            return new ParsedChatRequest(model, false, new ProviderChatRequest([]), ["Embeddings"],
                inputs.Sum(Encoding.UTF8.GetByteCount), credential, hybrid, routing,
                Operation: "embeddings", EmbeddingRequest: new ProviderEmbeddingRequest(inputs));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new GatewayRequestException("Invalid embeddings JSON or field type."); }
    }

    private static void Fields(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new GatewayRequestException("An object is required.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name))
                throw new GatewayRequestException("Unknown or duplicate embeddings field.", "unsupported_parameter");
    }
}
