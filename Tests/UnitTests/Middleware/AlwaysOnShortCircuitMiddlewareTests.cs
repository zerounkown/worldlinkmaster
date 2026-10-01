using Microsoft.AspNetCore.Http;
using WorldLinkMaster.Web.Middleware;

namespace WorldLinkMaster.Tests.UnitTests.Middleware;

/// <summary>
/// AlwaysOnShortCircuitMiddleware: answers Azure App Service's "Always On" ping (GET "/" with
/// User-Agent "AlwaysOn") directly with a 200 OK, without ever calling into the rest of the
/// pipeline — see the egress investigation in the perf/reduce-db-egress PR description for why
/// that ping used to run the full home page query/render pipeline on every hit.
/// </summary>
public class AlwaysOnShortCircuitMiddlewareTests
{
    private static (AlwaysOnShortCircuitMiddleware Middleware, Func<bool> NextWasCalled) CreateMiddleware()
    {
        var nextWasCalled = false;
        var middleware = new AlwaysOnShortCircuitMiddleware(_ =>
        {
            nextWasCalled = true;
            return Task.CompletedTask;
        });
        return (middleware, () => nextWasCalled);
    }

    private static DefaultHttpContext CreateContext(string path, string? userAgent)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (userAgent != null)
        {
            context.Request.Headers.UserAgent = userAgent;
        }
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task RootPath_AlwaysOnUserAgent_ShortCircuitsWith200_AndNeverCallsNext()
    {
        var (middleware, nextWasCalled) = CreateMiddleware();
        var context = CreateContext("/", "AlwaysOn");

        await middleware.InvokeAsync(context);

        Assert.False(nextWasCalled());
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task RootPath_RealBrowserUserAgent_PassesThroughToNext()
    {
        var (middleware, nextWasCalled) = CreateMiddleware();
        var context = CreateContext("/", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        await middleware.InvokeAsync(context);

        Assert.True(nextWasCalled());
    }

    [Fact]
    public async Task RootPath_NoUserAgent_PassesThroughToNext()
    {
        var (middleware, nextWasCalled) = CreateMiddleware();
        var context = CreateContext("/", userAgent: null);

        await middleware.InvokeAsync(context);

        Assert.True(nextWasCalled());
    }

    [Fact]
    public async Task NonRootPath_AlwaysOnUserAgent_PassesThroughToNext()
    {
        // A real visitor's browser could in principle send a custom User-Agent — the path check
        // is what keeps this scoped to exactly the Always On ping and nothing else.
        var (middleware, nextWasCalled) = CreateMiddleware();
        var context = CreateContext("/Products", "AlwaysOn");

        await middleware.InvokeAsync(context);

        Assert.True(nextWasCalled());
    }

    [Fact]
    public async Task RootPath_UserAgentCaseMismatch_PassesThroughToNext()
    {
        // The match is deliberately exact (ordinal), not case-insensitive — Azure's own probe
        // sends exactly "AlwaysOn", and keeping the match narrow avoids ever misidentifying a
        // real visitor whose client happens to send a similar-looking UA string.
        var (middleware, nextWasCalled) = CreateMiddleware();
        var context = CreateContext("/", "alwayson");

        await middleware.InvokeAsync(context);

        Assert.True(nextWasCalled());
    }
}
