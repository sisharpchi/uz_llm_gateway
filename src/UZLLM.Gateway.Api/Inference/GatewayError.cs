namespace UZLLM.Gateway.Api.Inference;

public sealed record GatewayError(int Status, string Code, string Type, string Message,
    Guid? OriginalRequestId = null);

public static class GatewayErrorResponse
{
    public static async Task WriteAsync(HttpContext context, GatewayError error,
        CancellationToken cancellationToken)
    {
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
