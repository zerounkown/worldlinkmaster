using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.E2E.Journeys;

/// <summary>
/// Bug report: the Apparel mega-menu shows T-Shirts, Pants, Jackets &amp; Outerwear and Uniforms
/// &amp; Shirts, but a published product in a subcategory not on that list (e.g. a "Headwear"
/// subcategory holding the real "Tactical Boonie", SKU F5519) was unreachable from the menu.
///
/// Root cause: every OTHER category's mega-menu is built by the generic BuildMegaColumns
/// (Views/Shared/_Layout.cshtml), which iterates the category's actual Subcategories and already
/// has a "leftovers" fallback for anything not in its hand-curated column grouping. Apparel
/// bypasses that generic builder entirely in favor of its own fully hand-curated
/// BuildApparelMenuAsync — a fixed 8-entry list (Featured/T-Shirts/Pants/Jackets &amp;
/// Outerwear/Uniforms &amp; Shirts/Shop by Use/Shop by Size/Colors &amp; Brands) built from
/// hardcoded subcategory slugs and product attribute values, never from the Subcategories
/// collection itself. A subcategory not referenced by any of those hardcoded links can never
/// appear, no matter how many published products it has.
///
/// Fix: BuildApparelMenuAsync now also computes "used" subcategory ids from everything the
/// hardcoded columns already link to, and adds one auto-generated column (same shape as Pants —
/// a header and a single "View All" link) for every OTHER subcategory with published products —
/// same leftovers idea BuildMegaColumns already uses, applied to Apparel too. The desktop column
/// layout (apparelMenuColumnGroups) extends itself to give each auto-generated column its own
/// physical column; the mobile side-nav already rendered every entry in the list generically, so
/// it needed no changes at all.
///
/// This also exercises IStorefrontCacheService end to end: the mega-menu data (including which
/// subcategories count as "has published products") is cached for 10 minutes, so this test
/// forces a real cache invalidation through an actual admin write (resaving the Apparel category
/// via the real Admin UI, logged in as the seeded admin account) before asserting — otherwise an
/// earlier test in this same shared app process could have already warmed the cache before this
/// subcategory/product existed, making the test flaky depending on run order rather than actually
/// broken.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class ApparelMegaMenuHeadwearTests : E2ETestBase
{
    private const string AdminEmail = "sales@wlinkmasters.com";
    private const string AdminPassword = "Admin@12345";
    private const string HeadwearSlug = "e2e-mega-menu-headwear";
    private const string ProductSlug = "e2e-mega-menu-boonie-hat";

    private int _apparelCategoryId;
    private int _headwearSubcategoryId;

    public ApparelMegaMenuHeadwearTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedHeadwearAsync();
    }

    private async Task SeedHeadwearAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(App.ConnectionString).Options;
        await using var context = new ApplicationDbContext(options);

        var apparel = await context.Categories.FirstAsync(c => c.Slug == "tactical-apparel");
        _apparelCategoryId = apparel.Id;

        var existing = await context.Subcategories.FirstOrDefaultAsync(s => s.Slug == HeadwearSlug);
        if (existing != null)
        {
            _headwearSubcategoryId = existing.Id; // Already seeded by a previous run in this shared schema.
            return;
        }

        var headwear = new Subcategory { Name = "Headwear", Slug = HeadwearSlug, CategoryId = apparel.Id };
        context.Subcategories.Add(headwear);
        await context.SaveChangesAsync();
        _headwearSubcategoryId = headwear.Id;

        var merchant = await context.Merchants.FirstAsync();
        context.Products.Add(new Product
        {
            Name = "E2E Boonie Hat",
            Slug = ProductSlug,
            Sku = "E2E-HEADWEAR-001",
            Price = 59.99m,
            StockQuantity = 20,
            IsPublished = true,
            CategoryId = apparel.Id,
            SubcategoryId = headwear.Id,
            MerchantId = merchant.Id
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Logs in as the seeded admin and resaves the Apparel category's own Edit form unchanged —
    /// the simplest real admin write that reaches IStorefrontCacheService.Invalidate(), forcing
    /// the next page load to recompute the mega-menu from current data instead of whatever an
    /// earlier test in this shared process may have already cached.
    /// </summary>
    private async Task LogInAsAdminAndInvalidateMenuCacheAsync()
    {
        await Page.GotoAsync(Url("Identity/Account/Login"));
        await Page.FillAsync("#Input_Email", AdminEmail);
        await Page.FillAsync("#Input_Password", AdminPassword);
        await Page.ClickAsync("#login-submit");
        await Page.WaitForURLAsync(url => !url.Contains("/Identity/Account/Login"));

        await Page.GotoAsync(Url($"Admin/Categories/Edit/{_apparelCategoryId}"));
        await Page.ClickAsync("button[type=submit]:has-text('Save')");
        await Page.WaitForURLAsync(url => url.Contains("/Admin/Categories"));
    }

    [Fact]
    public async Task DesktopMegaMenu_ShowsHeadwearColumn_ExistingColumnsStillPresent_ViewAllLinksToFilteredListing()
    {
        await LogInAsAdminAndInvalidateMenuCacheAsync();

        await Page.GotoAsync(BaseUrl);
        var apparelTrigger = Page.Locator("a.main-nav-link", new PageLocatorOptions { HasText = "Apparel" });
        await apparelTrigger.HoverAsync();

        var headwearColumn = Page.Locator(".main-nav-mega-group", new PageLocatorOptions { HasText = "Headwear" });
        await Assertions.Expect(headwearColumn).ToBeVisibleAsync();

        // The fix must not have removed or hidden any of the existing hand-curated columns.
        await Assertions.Expect(Page.Locator(".main-nav-mega-group", new PageLocatorOptions { HasText = "Pants" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".main-nav-mega-group", new PageLocatorOptions { HasText = "T-Shirts" })).ToBeVisibleAsync();

        var viewAllLink = headwearColumn.Locator("a.main-nav-mega-subitem");
        await Assertions.Expect(viewAllLink).ToHaveCountAsync(1);
        await viewAllLink.ClickAsync();

        await Page.WaitForURLAsync(url => url.Contains("/Products") && url.Contains($"subcategoryId={_headwearSubcategoryId}"));
        // Scoped to the product grid's own card title (.product-grid h3 a) — a bare page-wide
        // text match also hits the side-nav's .category-preview-name for this same product
        // (the layout's own categoryPreviews row renders on every page, including this one).
        await Assertions.Expect(Page.Locator(".product-grid h3", new PageLocatorOptions { HasText = "E2E Boonie Hat" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task MobileSideNav_ShowsHeadwearColumn_WithWorkingViewAllLink()
    {
        await LogInAsAdminAndInvalidateMenuCacheAsync();
        await Page.SetViewportSizeAsync(375, 667);

        await Page.GotoAsync(BaseUrl);
        await Page.ClickAsync("#sideNavToggle");

        var apparelSideNavItem = Page.Locator(".side-nav-item", new PageLocatorOptions { HasText = "Apparel" }).First;
        await apparelSideNavItem.Locator(".side-nav-preview-toggle").ClickAsync();

        var headwearGroup = apparelSideNavItem.Locator(".side-nav-subgroup", new LocatorLocatorOptions { HasText = "Headwear" });
        await Assertions.Expect(headwearGroup).ToBeVisibleAsync();

        var viewAllLink = headwearGroup.Locator("a");
        await Assertions.Expect(viewAllLink).ToHaveCountAsync(1);
        await viewAllLink.ClickAsync();

        await Page.WaitForURLAsync(url => url.Contains("/Products") && url.Contains($"subcategoryId={_headwearSubcategoryId}"));
        // Same scoping reason as the desktop test above: avoid colliding with the layout's own
        // side-nav category-preview-name for this product, which renders on this page too.
        await Assertions.Expect(Page.Locator(".product-grid h3", new PageLocatorOptions { HasText = "E2E Boonie Hat" })).ToBeVisibleAsync();
    }
}
