using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Tests.UnitTests.Services;

public class AnonymousOnlyOutputCachePolicyTests
{
    private static OutputCacheContext CreateContext(string method = "GET", string path = "/Products")
    {
        var httpContext = new DefaultHttpContext
        {
            Request = { Method = method, Path = path }
        };
        return new OutputCacheContext { HttpContext = httpContext };
    }

    // This is the regression this policy previously failed: CacheVaryByRules.QueryKeys defaults
    // to empty, and an empty QueryKeys means the cache key omits the query string entirely - not
    // "vary by everything". Without the "*" wildcard set here, /Products?categoryId=1 and
    // /Products?categoryId=3 (or two different Details?slug=... values) collapsed onto the same
    // cache entry, and anonymous visitors were served whichever page happened to be cached first.
    [Fact]
    public async Task CacheRequestAsync_AnonymousGet_VariesByFullQueryString()
    {
        var context = CreateContext();
        var policy = new AnonymousOnlyOutputCachePolicy(TimeSpan.FromSeconds(60), "products");

        await policy.CacheRequestAsync(context, CancellationToken.None);

        Assert.True(context.EnableOutputCaching);
        Assert.Single(context.CacheVaryByRules.QueryKeys);
        Assert.Equal("*", context.CacheVaryByRules.QueryKeys[0]);
    }

    [Fact]
    public async Task CacheRequestAsync_AnonymousGet_AlsoVariesByCultureAndCurrency()
    {
        var context = CreateContext();
        var policy = new AnonymousOnlyOutputCachePolicy(TimeSpan.FromSeconds(60), "products");

        await policy.CacheRequestAsync(context, CancellationToken.None);

        // QueryKeys and VaryByValues are independent, additive rules (confirmed against the
        // real OutputCacheKeyProvider) - setting one must not come at the expense of the other.
        Assert.True(context.CacheVaryByRules.VaryByValues.ContainsKey("culture"));
        Assert.True(context.CacheVaryByRules.VaryByValues.ContainsKey("currency"));
    }

    [Fact]
    public async Task CacheRequestAsync_NonGetRequest_DoesNotEnableCaching()
    {
        var context = CreateContext(method: "POST");
        var policy = new AnonymousOnlyOutputCachePolicy(TimeSpan.FromSeconds(60), "products");

        await policy.CacheRequestAsync(context, CancellationToken.None);

        Assert.False(context.EnableOutputCaching);
    }
}
