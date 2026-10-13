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
                    Description = "Implemented OpenAI-compatible models/chat contract. Responses and embeddings are not yet supported. "
                        + "Breaking changes require a versioned path and 180 days of migration overlap." };
                document.Servers = [new() { Url = "/" }];
                document.Components ??= new();
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["GatewayKey"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http,
                        Scheme = "bearer", Description = "Project-scoped UZLLM gateway key; never a provider key." }
                };
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
        if (!chat && context.Description.RelativePath != "v1/models")
            throw new InvalidOperationException("Document the new gateway operation before publishing it.");
        operation.Security = [new OpenApiSecurityRequirement
            { [new OpenApiSecuritySchemeReference("GatewayKey", context.Document)] = [] }];
        operation.Responses = new();
        operation.Responses["200"] = new OpenApiResponse { Description = "Successful inference/model discovery.",
            Content = new Dictionary<string, OpenApiMediaType>
            { ["application/json"] = new() { Schema = chat ? Completion() : Models() } } };
        foreach (var status in chat ? new[] { 400, 401, 402, 403, 409, 413, 415, 429, 502, 503 } : [401, 403, 503])
            operation.Responses[status.ToString()] = new OpenApiResponse
            {
                Description = "OpenAI error envelope. No retry/fallback is implied by an HTTP status alone.",
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = Error() } }
            };
        foreach (OpenApiResponse response in operation.Responses.Values)
            response.Headers = new Dictionary<string, IOpenApiHeader>
            { ["X-Request-Id"] = new OpenApiHeader { Schema = String(), Description = "Correlation ID." } };
        if (chat)
        {
            operation.Description = "Bounded chat subset; unsupported fields return 400. max_tokens and max_completion_tokens "
                + "are mutually exclusive. Tool/vision/structured-output capability is checked against the selected mapping. "
                + "After output begins, no replay/fallback occurs. Unknown usage leaves settlement pending, never a zero-cost claim.";
            operation.RequestBody = new OpenApiRequestBody { Required = true,
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new()
                { Schema = Request(), Example = JsonNode.Parse("""{"model":"example-chat","messages":[{"role":"user","content":"Hello"}],"max_completion_tokens":32,"stream":false}""") } } };
            operation.Parameters = [new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header,
                Required = false, Description = "Non-empty UUID, scoped to organization/key/operation for 24 hours. "
                    + "Replay returns 409 and X-Original-Request-Id; completion bodies are not replayed.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } }];
            operation.Responses["200"].Content!["text/event-stream"] = new OpenApiMediaType
            { Schema = String(), Example = JsonValue.Create(
                "data: {\"id\":\"chatcmpl-11111111111141118111111111111111\",\"object\":\"chat.completion.chunk\",\"created\":1790812800,\"model\":\"example-chat\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\"},\"finish_reason\":null}]}\n\n"
                + "data: {\"id\":\"chatcmpl-11111111111141118111111111111111\",\"object\":\"chat.completion.chunk\",\"created\":1790812800,\"model\":\"example-chat\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n"
                + "data: [DONE]\n\n") };
            operation.Responses["200"].Description = "JSON completion or SSE data frames. A complete stream ends with data: [DONE]. "
                + "Interrupted streams use event: error with the error envelope; HTTP status cannot change after headers. "
                + "Usage/finish/tool/refusal chunks may appear before the terminal marker.";
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
