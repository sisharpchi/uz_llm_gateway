using System.Data.Common;
using UZLLM.Modules.Billing.Contracts;
using UZLLM.Modules.Usage.Contracts;

namespace UZLLM.Management.Api.ReportingEvidence;

public sealed record ReportingEvidenceApiLimits(int DeadlineMilliseconds = 30_000);
public static class ReportingEvidenceApiBounds
{
    public static IApplicationBuilder UseUzllmReportingEvidenceApiBounds(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var parts = http.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: >= 6 } || !parts[0].Equals("management", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("v1", StringComparison.OrdinalIgnoreCase) || !parts[2].Equals("admin", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("organizations", StringComparison.OrdinalIgnoreCase) || !parts[5].Equals("reporting-evidence", StringComparison.OrdinalIgnoreCase))
        { await next(http); return; }
        var abort = http.RequestAborted; using var deadline = CancellationTokenSource.CreateLinkedTokenSource(abort);
        var ms = http.RequestServices.GetService<ReportingEvidenceApiLimits>()?.DeadlineMilliseconds ?? 30_000;
        if (ms is < 1 or > 30_000) throw new InvalidOperationException("Invalid evidence deadline.");
        deadline.CancelAfter(ms); http.RequestAborted = deadline.Token;
        try
        {
            if (HttpMethods.IsPost(http.Request.Method))
            {
                if (http.Request.ContentLength > 8192) { await Error(http, 413, "Evidence command too large"); return; }
                var bytes = new byte[8193]; var length = 0;
                while (length < bytes.Length)
                { var read = await http.Request.Body.ReadAsync(bytes.AsMemory(length), deadline.Token); if (read == 0) break; length += read; }
                if (length > 8192) { await Error(http, 413, "Evidence command too large"); return; }
                var original = http.Request.Body; using var buffered = new MemoryStream(bytes, 0, length, false);
                http.Request.Body = buffered; try { await next(http); } finally { http.Request.Body = original; }
            }
            else await next(http);
        }
        catch (ReportingEvidenceSessionException) { http.Response.StatusCode = 401; }
        catch (UnauthorizedAccessException) { http.Response.StatusCode = 403; }
        catch (KeyNotFoundException) { http.Response.StatusCode = 404; }
        catch (ArgumentException) { await Error(http, 400, "Invalid reporting evidence request"); }
        catch (Exception ex) when (ex is ReportingEvidenceConflictException or ReportingProfileUnavailableException)
        { await Error(http, 409, "Evidence terms conflict or approved profile unavailable"); }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested && deadline.IsCancellationRequested)
        { http.RequestAborted = abort; await Error(http, 504, "Evidence deadline exceeded"); }
        catch (Exception ex) when (ex is DbException or TimeoutException or InvalidOperationException or SliReportRejectedException
            or ReportingEvidenceDependencyException or System.Text.Json.JsonException)
        { await Error(http, 503, "Reporting evidence dependency unavailable"); }
        finally { http.RequestAborted = abort; }
    });
    private static Task Error(HttpContext http, int status, string title) => TypedResults.Problem(statusCode: status, title: title).ExecuteAsync(http);
}
