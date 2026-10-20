namespace UZLLM.Gateway.Api.Inference;

public sealed record GatewayError(int Status, string Code, string Type, string Message,
    Guid? OriginalRequestId = null);

public static class GatewayErrorResponse
{
    public static async Task WriteAsync(HttpContext context, GatewayError error,
        CancellationToken cancellationToken)
    {
        if (UZLLM.Observability.ExecutionObservationState.From(context) is { } observation)
        {
            if (error.Status >= 500 && error.Code != "provider_error") observation.GatewayFault = true;
            // The current limiter does not expose whether an operator ceiling or
            // customer policy bound fired. Keep 429 ambiguous, never exclude it.
            else if (error.Status is >= 400 and < 500 && error.Status != 429) observation.PolicyRejection = true;
        }
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = error.Status;
        context.Response.ContentType = "application/json";
        if (error.OriginalRequestId is { } original)
            context.Response.Headers["X-Original-Request-Id"] = original.ToString("N");
        await context.Response.WriteAsJsonAsync(new
        {
            error = new { message = error.Message, type = error.Type,
                code = error.Code, param = (string?)null }
        }, cancellationToken);
    }
}
