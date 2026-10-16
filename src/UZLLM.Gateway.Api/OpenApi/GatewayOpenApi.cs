using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UZLLM.Gateway.Api.OpenApi;

public static class GatewayOpenApi
{
    public static IServiceCollection AddGatewayOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.ShouldInclude = description => description.RelativePath?.StartsWith("v1/", StringComparison.Ordinal) == true;
            options.AddOperationTransformer<GatewayOperationTransformer>();
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new() { Title = "UZLLM Gateway API", Version = "1.0.0",
                    Description = "Implemented OpenAI-compatible models/chat, stateless text Responses JSON/SSE and bounded text/float embeddings. "
                        + "Breaking changes require a versioned path and 180 days of migration overlap." };
                document.Servers = [new() { Url = "/" }];
                document.Components ??= new();
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["GatewayKey"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http,
                        Scheme = "bearer", Description = "Project-scoped UZLLM gateway key; never a provider key." }
                };
                document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
                document.Components.Schemas["ResponsesStreamEvent"] = GatewayOperationTransformer.ResponseStreamEvent();
                return Task.CompletedTask;
            });
        });
        return services;
    }
}

public sealed class GatewayOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var chat = context.Description.RelativePath == "v1/chat/completions";
        var responses = context.Description.RelativePath == "v1/responses";
        var embeddings = context.Description.RelativePath == "v1/embeddings";
        var inference = chat || responses || embeddings;
        if (!inference && context.Description.RelativePath != "v1/models")
            throw new InvalidOperationException("Document the new gateway operation before publishing it.");
        operation.Security = [new OpenApiSecurityRequirement
            { [new OpenApiSecuritySchemeReference("GatewayKey", context.Document)] = [] }];
        operation.Responses = new();
        operation.Responses["200"] = new OpenApiResponse { Description = "Successful inference/model discovery.",
            Content = new Dictionary<string, OpenApiMediaType>
            { ["application/json"] = new() { Schema = embeddings ? Embeddings() : responses ? Response() : chat ? Completion() : Models() } } };
        foreach (var status in inference ? new[] { 400, 401, 402, 403, 404, 409, 413, 415, 429, 502, 503 } : [401, 403, 503])
            operation.Responses[status.ToString()] = new OpenApiResponse
            {
                Description = "OpenAI error envelope. No retry/fallback is implied by an HTTP status alone.",
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = Error() } }
            };
        foreach (OpenApiResponse response in operation.Responses.Values)
            response.Headers = new Dictionary<string, IOpenApiHeader>
            { ["X-Request-Id"] = new OpenApiHeader { Schema = String(), Description = "Correlation ID." } };
        if (inference)
        {
            operation.Description = embeddings
                ? "OpenAI native text/float subset: one string or 1–32 strings; each nonblank input <=8192 UTF-8 bytes and catalog context. "
                    + "No token-ID inputs, base64, dimensions, stream, user or cross-model fallback. Only priority/price routing; "
                    + "override incompatible project strategy explicitly. Existing tenant/key/financial/Managed/BYOK/Hybrid controls apply. "
                    + "Input-only verified usage, one request fee per batch; unknown usage is null/pending reconciliation, not zero. "
                    + "Idempotency operation is embeddings, independent of chat.completions/responses."
                : responses
                ? "Stateless text Responses JSON/SSE subset, translated through existing chat adapters. input accepts text or 1–256 "
                    + "text message items (including instructions). stream is optional boolean; store must be false or omitted; no conversation retrieval, "
                    + "tools, vision, structured-output or reasoning controls. Unsupported fields return 400 before reservation/dispatch. "
                    + "Missing usage is null and remains pending evidence, not zero cost. Existing authorization, limits, routing, "
                    + "BYOK/Hybrid consent and financial controls apply. Idempotency operation is responses, separate from chat.completions."
                : "Bounded chat subset; unsupported fields return 400. max_tokens and max_completion_tokens "
                + "are mutually exclusive. Tool/vision/structured-output capability is checked against the selected mapping. "
                + "After output begins, no replay/fallback occurs. Unknown usage leaves settlement pending, never a zero-cost claim.";
            operation.RequestBody = new OpenApiRequestBody { Required = true,
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new()
                { Schema = embeddings ? EmbeddingsRequest() : responses ? ResponseRequest() : Request(), Example = JsonNode.Parse(embeddings
                    ? """{"model":"example-embedding","input":["Hello","World"],"encoding_format":"float"}"""
                    : responses
                    ? """{"model":"example-chat","input":"Hello","max_output_tokens":32,"store":false}"""
                    : """{"model":"example-chat","messages":[{"role":"user","content":"Hello"}],"max_completion_tokens":32,"stream":false}""") } } };
            operation.Parameters = [new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header,
                Required = false, Description = "Non-empty UUID, scoped to organization/key/operation for 24 hours. "
                    + "Replay returns 409 and X-Original-Request-Id; completion bodies are not replayed.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } }];
            if (chat)
            {
                operation.Responses["200"].Content!["text/event-stream"] = new OpenApiMediaType
                { Schema = String(), Example = JsonValue.Create(
                    "data: {\"id\":\"chatcmpl-11111111111141118111111111111111\",\"object\":\"chat.completion.chunk\",\"created\":1790812800,\"model\":\"example-chat\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\"},\"finish_reason\":null}]}\n\n"
                    + "data: {\"id\":\"chatcmpl-11111111111141118111111111111111\",\"object\":\"chat.completion.chunk\",\"created\":1790812800,\"model\":\"example-chat\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n"
                    + "data: [DONE]\n\n") };
                operation.Responses["200"].Description = "JSON completion or SSE data frames. A complete stream ends with data: [DONE]. "
                    + "Interrupted streams use event: error with the error envelope; HTTP status cannot change after headers. "
                    + "Usage/finish/tool/refusal chunks may appear before the terminal marker.";
            }
            else if (responses)
            {
                operation.Responses["200"].Description = "JSON response or named Responses SSE events (ResponsesStreamEvent schema), not chat chunks/[DONE]. "
                    + "Sequence numbers start at zero. Terminal response.completed/incomplete follows upstream closure and financial cleanup; "
                    + "response.failed reports partial-stream/finalization errors. Unknown usage is null, never zero. No fallback after output. "
                    + "Streaming terminal snapshots retain at most 1048576 text/refusal characters; exceeding the bound fails safely, never truncates as success.";
                operation.Responses["200"].Content!["text/event-stream"] = new OpenApiMediaType { Schema = String() };
                foreach (var response in operation.Responses.Values)
                    response.Headers!["Cache-Control"] = new OpenApiHeader { Schema = Enum("no-store") };
            }
            else if (embeddings)
            {
                operation.Responses["200"].Description = "Ordered float vectors; usage is prompt_tokens=total_tokens or null when evidence is unknown. No SSE.";
                foreach (var response in operation.Responses.Values)
                    response.Headers!["Cache-Control"] = new OpenApiHeader { Schema = Enum("no-store") };
            }
            foreach (var name in new[] { "X-Uzllm-Provider", "X-Uzllm-Model", "X-Uzllm-Requested-Model", "X-Uzllm-Billing-Mode" })
                operation.Responses["200"].Headers![name] = new OpenApiHeader { Schema = String() };
            operation.Responses["409"].Headers!["X-Original-Request-Id"] = new OpenApiHeader { Schema = String() };
            operation.Responses["429"].Headers!["Retry-After"] = new OpenApiHeader { Schema = String() };
        }
        return Task.CompletedTask;
    }

    private static OpenApiSchema String() => new() { Type = JsonSchemaType.String };
    private static OpenApiSchema Integer() => new() { Type = JsonSchemaType.Integer, Format = "int64" };
    private static OpenApiSchema Nullable(OpenApiSchema schema) { schema.Type |= JsonSchemaType.Null; return schema; }
    private static OpenApiSchema Array(IOpenApiSchema items) => new() { Type = JsonSchemaType.Array, Items = items };
    private static OpenApiSchema Object(params (string Name, IOpenApiSchema Schema)[] properties) => new()
    { Type = JsonSchemaType.Object, Properties = properties.ToDictionary(item => item.Name, item => item.Schema) };
    private static OpenApiSchema Required(OpenApiSchema schema, params string[] names)
    { schema.Required = names.ToHashSet(StringComparer.Ordinal); return schema; }
    private static OpenApiSchema Enum(params string[] values) => new()
    { Type = JsonSchemaType.String, Enum = values.Select(value => (JsonNode)JsonValue.Create(value)!).ToList() };

    public static OpenApiSchema Error() => Required(Object(("error", Required(Object(
        ("message", String()), ("type", String()), ("code", String()), ("param", Nullable(String()))),
        "message", "type", "code", "param"))), "error");

    public static OpenApiSchema EmbeddingsRequest()
    {
        var text = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 8192, Pattern = "\\S" };
        var extension = Object(("provider_key_id", new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }),
            ("allow_managed_fallback", new OpenApiSchema { Type = JsonSchemaType.Boolean }), ("routing", Enum("priority", "price")));
        extension.AdditionalPropertiesAllowed = false;
        var request = Required(Object(("model", new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 200, Pattern = "\\S" }),
            ("input", new OpenApiSchema { OneOf = [text, new OpenApiSchema { Type = JsonSchemaType.Array, Items = text, MinItems = 1, MaxItems = 32 }] }),
            ("encoding_format", Enum("float")), ("uzllm", extension)), "model", "input");
        request.AdditionalPropertiesAllowed = false;
        request.Description = "Unique JSON properties; UTF-8 byte (not character) bounds and model context enforced at runtime. Hybrid requires nonempty provider_key_id.";
        return request;
    }

    public static OpenApiSchema Embeddings() => Required(Object(("object", Enum("list")), ("model", String()),
        ("data", new OpenApiSchema { Type = JsonSchemaType.Array, MinItems = 1, MaxItems = 32,
            Items = Required(Object(("object", Enum("embedding")), ("index", new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0", Maximum = "31" }),
                ("embedding", new OpenApiSchema { Type = JsonSchemaType.Array, MinItems = 1, MaxItems = 4096,
                    Items = new OpenApiSchema { Type = JsonSchemaType.Number, Format = "float" } })), "object", "index", "embedding") }),
        ("usage", Nullable(Required(Object(("prompt_tokens", Integer()), ("total_tokens", Integer())), "prompt_tokens", "total_tokens")))), "object", "model", "data", "usage");

    public static OpenApiSchema ResponseRequest()
    {
        var text = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 1_048_576, Pattern = "\\S" };
        var part = Required(Object(("type", Enum("input_text")), ("text", text)), "type", "text");
        part.AdditionalPropertiesAllowed = false;
        var message = Required(Object(("type", Enum("message")),
            ("role", Enum("system", "developer", "user", "assistant")),
            ("content", new OpenApiSchema { OneOf = [text, new OpenApiSchema { Type = JsonSchemaType.Array,
                Items = part, MinItems = 1, MaxItems = 64 }] })), "role", "content");
        message.AdditionalPropertiesAllowed = false;
        var chat = Request();
        var request = Required(Object(("model", chat.Properties!["model"]),
            ("input", new OpenApiSchema { OneOf = [text, new OpenApiSchema { Type = JsonSchemaType.Array,
                Items = message, MinItems = 1, MaxItems = 256 }] }), ("instructions", text),
            ("max_output_tokens", new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "16", Maximum = int.MaxValue.ToString() }),
            ("temperature", chat.Properties["temperature"]), ("top_p", chat.Properties["top_p"]),
            ("uzllm", chat.Properties["uzllm"]),
            ("stream", new OpenApiSchema { Type = JsonSchemaType.Boolean }),
            ("store", new OpenApiSchema { Type = JsonSchemaType.Boolean, Enum = [JsonValue.Create(false)!] })), "model", "input");
        request.AdditionalPropertiesAllowed = false;
        request.Description = "Stateless text subset. Instructions plus input count <=256; JSON property names must be unique at each object.";
        return request;
    }

    public static OpenApiSchema ResponseStreamEvent()
    {
        var snapshot = Response();
        snapshot.Properties!["status"] = Enum("in_progress", "completed", "incomplete", "failed");
        snapshot.Properties["error"] = Nullable(Required(Object(("code", String()), ("message", String())), "code", "message"));
        var message = (OpenApiSchema)snapshot.Properties["output"].Items!;
        message.Properties!["status"] = Enum("in_progress", "completed", "incomplete");
        var part = message.Properties["content"].Items!;
        OpenApiSchema Event(string[] types, params (string Name, IOpenApiSchema Schema)[] fields) =>
            Required(Object([("type", Enum(types)), ("sequence_number", new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0" }), ..fields]),
                ["type", "sequence_number", ..fields.Select(field => field.Name)]);
        (string, IOpenApiSchema)[] address = [("item_id", String()), ("output_index", Integer()), ("content_index", Integer())];
        return new OpenApiSchema { OneOf = [
            Event(["response.created", "response.in_progress", "response.completed", "response.incomplete", "response.failed"], ("response", snapshot)),
            Event(["response.output_item.added", "response.output_item.done"], ("output_index", Integer()), ("item", message)),
            Event(["response.content_part.added", "response.content_part.done"], [..address, ("part", part)]),
            Event(["response.output_text.delta"], [..address, ("delta", String()), ("logprobs", Array(new OpenApiSchema { Type = JsonSchemaType.Object }))]),
            Event(["response.refusal.delta"], [..address, ("delta", String())]),
            Event(["response.output_text.done"], [..address, ("text", String()), ("logprobs", Array(new OpenApiSchema { Type = JsonSchemaType.Object }))]),
            Event(["response.refusal.done"], [..address, ("refusal", String())]) ] };
    }

    public static OpenApiSchema Response()
    {
        var content = new OpenApiSchema { OneOf = [
            Required(Object(("type", Enum("output_text")), ("text", String()),
                ("annotations", Array(new OpenApiSchema { Type = JsonSchemaType.Object })),
                ("logprobs", Array(new OpenApiSchema { Type = JsonSchemaType.Object }))), "type", "text", "annotations", "logprobs"),
            Required(Object(("type", Enum("refusal")), ("refusal", String())), "type", "refusal")] };
        return Required(Object(("id", String()), ("object", Enum("response")), ("created_at", Integer()),
            ("status", Enum("completed", "incomplete")), ("error", new OpenApiSchema { Type = JsonSchemaType.Null }),
            ("incomplete_details", Nullable(Required(Object(("reason", Enum("max_output_tokens", "content_filter"))), "reason"))),
            ("model", String()), ("output", Array(Required(Object(("id", String()), ("type", Enum("message")),
                ("role", Enum("assistant")), ("status", Enum("completed", "incomplete")), ("content", Array(content))), "id", "type", "role", "status", "content"))),
            ("instructions", Nullable(String())), ("max_output_tokens", Nullable(Integer())),
            ("store", new OpenApiSchema { Type = JsonSchemaType.Boolean, Enum = [JsonValue.Create(false)!] }),
            ("previous_response_id", new OpenApiSchema { Type = JsonSchemaType.Null }),
            ("parallel_tool_calls", new OpenApiSchema { Type = JsonSchemaType.Boolean, Enum = [JsonValue.Create(false)!] }),
            ("reasoning", Required(Object(("effort", new OpenApiSchema { Type = JsonSchemaType.Null }),
                ("summary", new OpenApiSchema { Type = JsonSchemaType.Null })), "effort", "summary")),
            ("temperature", new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null }),
            ("top_p", new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.Null }),
            ("text", Required(Object(("format", Required(Object(("type", Enum("text"))), "type"))), "format")),
            ("tool_choice", Enum("none")), ("tools", Array(new OpenApiSchema { Type = JsonSchemaType.Object })),
            ("truncation", Enum("disabled")), ("metadata", new OpenApiSchema { Type = JsonSchemaType.Object }),
            ("usage", Nullable(Required(Object(("input_tokens", Integer()), ("output_tokens", Integer()), ("total_tokens", Integer()),
                ("input_tokens_details", Required(Object(("cached_tokens", Integer())), "cached_tokens")),
                ("output_tokens_details", Required(Object(("reasoning_tokens", Integer())), "reasoning_tokens"))),
                "input_tokens", "output_tokens", "total_tokens", "input_tokens_details", "output_tokens_details")))),
            "id", "object", "created_at", "status", "error", "incomplete_details", "model", "output", "usage", "store");
    }

    public static OpenApiSchema Request()
    {
        var function = Required(Object(("name", String()), ("description", String()),
            ("parameters", new OpenApiSchema { Type = JsonSchemaType.Object, AdditionalPropertiesAllowed = true })), "name", "parameters");
        var call = Required(Object(("id", String()), ("type", Enum("function")),
            ("function", Required(Object(("name", String()), ("arguments", String())), "name", "arguments"))), "id", "type", "function");
        var content = new OpenApiSchema { OneOf = [String(), new OpenApiSchema { Type = JsonSchemaType.Null }, Array(
            new OpenApiSchema { OneOf = [Required(Object(("type", Enum("text")), ("text", String())), "type", "text"),
                Required(Object(("type", Enum("image_url")), ("image_url", Required(Object(("url", String()),
                    ("detail", Enum("auto", "low", "high"))), "url"))), "type", "image_url")] })] };
        var message = Required(Object(("role", Enum("system", "developer", "user", "assistant", "tool")),
            ("content", content), ("tool_call_id", String()), ("tool_calls", Array(call))), "role");
        message.Description = "Content is required except assistant tool-call messages. Only user messages accept HTTPS image URLs; "
            + "only assistant messages accept tool_calls; tool messages require tool_call_id and one text content part.";
        var messages = Array(message); messages.MinItems = 1; messages.MaxItems = 256;
        var output = Integer(); output.Format = "int32"; output.Minimum = "1"; output.Maximum = int.MaxValue.ToString();
        var extensions = Object(("provider_key_id", new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }),
            ("allow_managed_fallback", new OpenApiSchema { Type = JsonSchemaType.Boolean }),
            ("routing", Enum("priority", "price", "latency", "throughput", "auto")),
            ("fallback_models", new OpenApiSchema { Type = JsonSchemaType.Array, Items = String(), MinItems = 1, MaxItems = 2, UniqueItems = true }));
        extensions.AdditionalPropertiesAllowed = false;
        extensions.Description = "provider_key_id must be non-empty. Managed fallback opt-in requires provider_key_id. "
            + "Fallback codes must be canonical and distinct from the requested model; BYOK cross-model fallback requires Managed opt-in.";
        var model = String(); model.MinLength = 1; model.MaxLength = 200;
        var streamOptions = Object(("include_usage", new OpenApiSchema { Type = JsonSchemaType.Boolean }));
        streamOptions.AdditionalPropertiesAllowed = false;
        var request = Required(Object(("model", model), ("messages", messages),
            ("temperature", new OpenApiSchema { Type = JsonSchemaType.Number, Minimum = "0", Maximum = "2" }),
            ("top_p", new OpenApiSchema { Type = JsonSchemaType.Number, ExclusiveMinimum = "0", Maximum = "1" }),
            ("max_tokens", output), ("max_completion_tokens", output),
            ("stream", new OpenApiSchema { Type = JsonSchemaType.Boolean }),
            ("stop", new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1 },
                new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1 }, MaxItems = 4 }] }),
            ("tools", new OpenApiSchema { Type = JsonSchemaType.Array, MaxItems = 64,
                Items = Required(Object(("type", Enum("function")), ("function", function)), "type", "function") }),
            ("tool_choice", new OpenApiSchema { OneOf = [Enum("auto", "none", "required"),
                Required(Object(("type", Enum("function")), ("function", Required(Object(("name", String())), "name"))), "type", "function")] }),
            ("response_format", Required(Object(("type", Enum("text", "json_object", "json_schema")),
                ("json_schema", Required(Object(("name", String()), ("schema", new OpenApiSchema { Type = JsonSchemaType.Object,
                    AdditionalPropertiesAllowed = true }), ("strict", new OpenApiSchema { Type = JsonSchemaType.Boolean })), "name", "schema"))), "type")),
            ("stream_options", streamOptions),
            ("uzllm", extensions)), "model", "messages");
        request.AdditionalPropertiesAllowed = false;
        request.Not = new OpenApiSchema { Required = new HashSet<string> { "max_tokens", "max_completion_tokens" } };
        return request;
    }

    public static OpenApiSchema Completion() => Required(Object(("id", String()), ("object", Enum("chat.completion")),
        ("created", Integer()), ("model", String()), ("choices", Array(Required(Object(("index", Integer()),
            ("message", Required(Object(("role", Enum("assistant")), ("content", Nullable(String())),
                ("refusal", Nullable(String())), ("tool_calls", new OpenApiSchema { Type = JsonSchemaType.Array | JsonSchemaType.Null,
                    Items = Required(Object(("id", String()), ("type", Enum("function")),
                        ("function", Required(Object(("name", String()), ("arguments", String())), "name", "arguments"))), "id", "type", "function") })),
                "role", "content", "refusal", "tool_calls")), ("finish_reason", Nullable(String()))), "index", "message", "finish_reason"))),
        ("usage", Nullable(Required(Object(("prompt_tokens", Integer()), ("completion_tokens", Integer()),
            ("total_tokens", Integer()), ("prompt_tokens_details", Required(Object(("cached_tokens", Integer())), "cached_tokens")),
            ("completion_tokens_details", Required(Object(("reasoning_tokens", Integer())), "reasoning_tokens"))),
            "prompt_tokens", "completion_tokens", "total_tokens", "prompt_tokens_details", "completion_tokens_details")))),
        "id", "object", "created", "model", "choices", "usage");

    public static OpenApiSchema Models() => Required(Object(("object", Enum("list")), ("data", Array(Required(Object(
        ("id", String()), ("object", Enum("model")), ("owned_by", Enum("uzllm")), ("name", String()),
        ("context_length", Integer()), ("max_output_tokens", Integer()), ("capabilities", Array(String())),
        ("pricing", Required(Object(("input_micro_usd_per_million", Integer()), ("output_micro_usd_per_million", Integer()),
            ("cached_input_micro_usd_per_million", Nullable(Integer()))), "input_micro_usd_per_million",
            "output_micro_usd_per_million", "cached_input_micro_usd_per_million"))),
        "id", "object", "owned_by", "name", "context_length", "max_output_tokens", "capabilities", "pricing")))), "object", "data");
}
