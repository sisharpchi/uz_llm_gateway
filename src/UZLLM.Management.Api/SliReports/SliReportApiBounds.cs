using System.Data.Common;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Management.Api.SliReports;

public sealed record SliReportApiLimits(int DeadlineMilliseconds = 30_000);
public static class SliReportApiBounds
{
    public static IApplicationBuilder UseUzllmSliReportApiBounds(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        if (!http.Request.Path.StartsWithSegments("/management/v1/admin/sli-reports", StringComparison.OrdinalIgnoreCase)) { await next(http); return; }
        var abort = http.RequestAborted;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(abort);
        var milliseconds = http.RequestServices.GetService<SliReportApiLimits>()?.DeadlineMilliseconds ?? 30_000;
        if (milliseconds is < 1 or > 30_000) throw new InvalidOperationException("Invalid SLI deadline.");
        deadline.CancelAfter(milliseconds); http.RequestAborted = deadline.Token;
        try
        {
            if (HttpMethods.IsPost(http.Request.Method))
            {
                if (http.Request.ContentLength > 8192) { await Error(http, 413, "Report command too large"); return; }
                var bytes = new byte[8193]; var length = 0;
                while (length < bytes.Length)
                {
                    var read = await http.Request.Body.ReadAsync(bytes.AsMemory(length), deadline.Token);
                    if (read == 0) break; length += read;
                }
                if (length > 8192) { await Error(http, 413, "Report command too large"); return; }
                var original = http.Request.Body; using var buffered = new MemoryStream(bytes, 0, length, false);
                http.Request.Body = buffered; try { await next(http); } finally { http.Request.Body = original; }
            }
            else await next(http);
        }
        catch (SliReportSessionRequiredException) { http.Response.StatusCode = 401; }
        catch (UnauthorizedAccessException) { http.Response.StatusCode = 403; }
        catch (ArgumentException) { await Error(http, 400, "Invalid SLI report request"); }
        catch (SliReportConflictException) { await Error(http, 409, "SLI command terms or generation conflict"); }
        catch (SliReportRejectedException exception) when (exception.Code == "Bounds") { await Error(http, 413, "Report too large"); }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested && deadline.IsCancellationRequested)
        { http.RequestAborted = abort; await Error(http, 504, "SLI report deadline exceeded"); }
        catch (Exception exception) when (exception is DbException or TimeoutException or InvalidOperationException or SliReportRejectedException)
        { await Error(http, 503, "SLI report dependency unavailable"); }
        finally { http.RequestAborted = abort; }
    });
    private static Task Error(HttpContext http, int status, string title) => TypedResults.Problem(statusCode: status, title: title).ExecuteAsync(http);
}
