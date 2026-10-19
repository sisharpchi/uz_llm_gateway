using System.Data.Common;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Organizations.Contracts;

namespace UZLLM.Management.Api.Statements;

public sealed record StatementApiLimits(int DeadlineMilliseconds = 30_000);

public static class StatementApiBounds
{
    public static IApplicationBuilder UseUzllmStatementApiBounds(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var path = http.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/management/v1/", StringComparison.Ordinal)
            || !(path.Contains("/statements", StringComparison.Ordinal) || path.Contains("/reports/", StringComparison.Ordinal)))
        { await next(http); return; }
        var abort = http.RequestAborted;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(abort);
        var milliseconds = http.RequestServices.GetService<StatementApiLimits>()?.DeadlineMilliseconds ?? 30_000;
        if (milliseconds is < 1 or > 30_000) throw new InvalidOperationException("Invalid statement deadline.");
        deadline.CancelAfter(milliseconds);
        http.RequestAborted = deadline.Token;
        try
        {
            // Middleware runs BEFORE Minimal API body binding, including for chunked input.
            if (HttpMethods.IsPost(http.Request.Method))
            {
                if (http.Request.ContentLength > 8192) { await Error(http, 413, "Report command too large"); return; }
                var bytes = new byte[8193];
                var length = 0;
                while (length < bytes.Length)
                {
                    var read = await http.Request.Body.ReadAsync(bytes.AsMemory(length), deadline.Token);
                    if (read == 0) break;
                    length += read;
                }
                if (length > 8192) { await Error(http, 413, "Report command too large"); return; }
                var original = http.Request.Body;
                using var buffered = new MemoryStream(bytes, 0, length, false);
                http.Request.Body = buffered;
                try { await next(http); } finally { http.Request.Body = original; }
            }
            else await next(http);
        }
        catch (StatementSessionRequiredException) { http.Response.StatusCode = 401; }
        catch (KeyNotFoundException) { http.Response.StatusCode = 404; }
        catch (Exception exception) when (exception is TenantAccessDeniedException or UnauthorizedAccessException)
        { http.Response.StatusCode = 403; }
        catch (ArgumentException) { await Error(http, 400, "Invalid statement request"); }
        catch (StatementCommandConflictException) { await Error(http, 409, "Statement command conflicts with recorded terms or generation"); }
        catch (StatementBuildRejectedException exception) when (exception.Code == "Bounds") { await Error(http, 413, "Statement too large"); }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested && deadline.IsCancellationRequested)
        { http.RequestAborted = abort; await Error(http, 504, "Statement deadline exceeded"); }
        catch (Exception exception) when (exception is DbException or TimeoutException or InvalidOperationException or StatementArtifactIntegrityException or StatementReadDependencyException)
        { await Error(http, 503, "Statement dependency unavailable"); }
        finally { http.RequestAborted = abort; }
    });

    private static Task Error(HttpContext http, int status, string title) =>
        TypedResults.Problem(statusCode: status, title: title).ExecuteAsync(http);
}
