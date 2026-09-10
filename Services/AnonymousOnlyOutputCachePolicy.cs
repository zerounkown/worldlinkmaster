using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.OutputCaching;

namespace WorldLinkMaster.Web.Services;

/// <summary>
/// Caches a response only for unauthenticated requests. _Layout.cshtml renders per-user chrome
/// (login/account state, cart count, wishlist, the support-chat widget) on every page, so caching
/// an authenticated response would leak one user's session chrome into whatever anonymous or
/// differently-authenticated visitor is served that cache entry next. Anonymous traffic is also
/// the overwhelming majority of storefront browsing (home/listing/detail), so restricting caching
/// to it still captures nearly all of the load-reduction benefit without that risk.
/// </summary>
public sealed class AnonymousOnlyOutputCachePolicy : IOutputCachePolicy
{
    private readonly TimeSpan _duration;
    private readonly string[] _tags;

    public AnonymousOnlyOutputCachePolicy(TimeSpan duration, params string[] tags)
    {
        _duration = duration;
        _tags = tags;
    }

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        if (httpContext.User.Identity?.IsAuthenticated == true
            || !HttpMethods.IsGet(httpContext.Request.Method))
        {
            context.EnableOutputCaching = false;
            return ValueTask.CompletedTask;
        }

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;
        context.ResponseExpirationTimeSpan = _duration;

        // The page's rendered HTML depends on two cookies beyond the URL itself: the UI
        // language (dir="rtl"/"ltr", every translated string) and the display currency
        // (every shown price). Without varying by these, the first anonymous visitor to
        // populate the cache — whatever language/currency they happened to be using —
        // would have that same render served to every other anonymous visitor regardless
        // of their own cookie, until the entry expired. There's no built-in VaryByCookie
        // list (only VaryByValues, a free-form key/value bag), so the cookie values are
        // read directly and fed into it.
        context.CacheVaryByRules.VaryByValues["culture"] = httpContext.Request.Cookies[CookieRequestCultureProvider.DefaultCookieName] ?? string.Empty;
        context.CacheVaryByRules.VaryByValues["currency"] = httpContext.Request.Cookies["currency"] ?? string.Empty;

        foreach (var tag in _tags)
        {
            context.Tags.Add(tag);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        // Only cache a genuinely successful, full page render — never an error page or a
        // redirect (e.g. a stale/invalid product slug bouncing through a 302 somewhere).
        var response = context.HttpContext.Response;
        if (response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
