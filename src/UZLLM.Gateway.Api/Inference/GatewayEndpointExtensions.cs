namespace UZLLM.Gateway.Api.Inference;

public static class GatewayEndpointExtensions
{
    public static IEndpointRouteBuilder MapUzllmInferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/models", async (HttpContext context,
            IInferenceGateway gateway, CancellationToken cancellationToken) =>
            await gateway.ListModelsAsync(context, cancellationToken))
            .WithName("ListGatewayModels")
            .WithSummary("List active models available for inference")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapPost("/v1/chat/completions", async (HttpContext context,
            IInferenceGateway gateway, CancellationToken cancellationToken) =>
            await gateway.ChatAsync(context, cancellationToken))
            .WithName("CreateChatCompletion")
            .WithSummary("Create an OpenAI-compatible chat completion or SSE stream")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }
}
