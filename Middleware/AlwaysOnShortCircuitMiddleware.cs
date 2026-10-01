namespace WorldLinkMaster.Web.Middleware;

// Azure App Service's "Always On" feature pings "/" on every instance on a fixed interval to
// keep the app warm, identifying itself with the User-Agent "AlwaysOn". Each ping used to run
// the full home page pipeline — database queries (HomeController.Welcome, plus every query
// Views/Shared/_Layout.cshtml ran), view rendering, the lot — and the output cache's per-
// instance in-memory TTL is often shorter than the actual ping interval, so most pings were a
// full cache miss anyway. Registered first in the pipeline (see Program.cs), this answers that
// exact ping directly with a tiny 200 OK before anything else runs — no database access, no MVC,
// no view rendering. Any request that doesn't match both the path and the exact User-Agent
// (a real visitor's browser, any other path, a differently-configured health probe) passes
// through to the rest of the pipeline completely unaffected.
public class AlwaysOnShortCircuitMiddleware
{
    private const string AlwaysOnUserAgent = "AlwaysOn";

    private readonly RequestDelegate _next;

    public AlwaysOnShortCircuitMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/", StringComparison.Ordinal) &&
            string.Equals(context.Request.Headers.UserAgent.ToString(), AlwaysOnUserAgent, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("OK");
            return;
        }

        await _next(context);
    }
}
