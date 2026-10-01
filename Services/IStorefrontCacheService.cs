using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

// Everything Views/Shared/_Layout.cshtml needs to build the mega-menu/side-nav, bundled into one
// cached object so a single IMemoryCache entry covers the whole layout render. Shaped to match
// exactly what the view used to compute inline (see StorefrontCacheService for the queries this
// replaces) so the view's own derivation logic (BuildMegaColumns, BuildApparelMenuAsync, URL
// building) didn't need to change, only where the raw data comes from.
public class NavMenuData
{
    public required List<Category> NavCategories { get; init; }
    public required HashSet<int> SubcategoriesWithPublishedProducts { get; init; }
    public required List<Product> QuickFilterProducts { get; init; }
    public required HashSet<(string Code, string ValueEn)> RealApparelAttrPairs { get; init; }
    public required Dictionary<int, List<Product>> CategoryPreviews { get; init; }
    public required Dictionary<string, Brand> ApparelMenuBrands { get; init; }
}

// Caches near-static reference data that Views/Shared/_Layout.cshtml (every storefront page) and
// ProductsController.Index (the color filter) used to re-query from Postgres on every single
// request. See the perf/reduce-db-egress PR description for the egress investigation this came
// out of. Backed by IMemoryCache, which is per-instance (no Redis dependency) — Invalidate() is
// called from every admin action that writes data the menu/listing reads, but with 2 App Service
// instances, only the instance that handled the write actually clears its own copy; the other
// instance keeps serving its stale copy until the TTL below expires. Keep the TTL short enough
// that this window is acceptable.
public interface IStorefrontCacheService
{
    Task<NavMenuData> GetNavMenuDataAsync();
    Task<List<ColorFamily>> GetColorFamiliesAsync();

    // Clears every cache entry this service owns on the instance that handles the call. Call
    // this immediately after saving any change that affects the mega-menu or the listing-page
    // color filter (see the XML doc on each admin action for exactly which ones).
    void Invalidate();
}
