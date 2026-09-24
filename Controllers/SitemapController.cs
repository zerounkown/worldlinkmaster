using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;

namespace WorldLinkMaster.Web.Controllers;

// One URL per page, not one per language — the site has no per-language URL segment (see
// Program.cs's RequestLocalizationOptions: cookie-only culture provider, same "/Products?..."
// URL serves both EN and AR depending on the visitor's cookie), so there's nothing to list as a
// language-specific alternate.
public class SitemapController : Controller
{
    private static readonly XNamespace SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private readonly ApplicationDbContext _context;

    public SitemapController(ApplicationDbContext context)
    {
        _context = context;
    }

    [Route("sitemap.xml")]
    [OutputCache(PolicyName = "Sitemap")]
    public async Task<IActionResult> Index()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var urlset = new XElement(SitemapNs + "urlset");

        void AddUrl(string path, string changefreq, string priority, DateTime? lastMod = null)
        {
            var url = new XElement(SitemapNs + "url",
                new XElement(SitemapNs + "loc", $"{baseUrl}{path}"),
                new XElement(SitemapNs + "changefreq", changefreq),
                new XElement(SitemapNs + "priority", priority));
            if (lastMod.HasValue)
            {
                url.Add(new XElement(SitemapNs + "lastmod", lastMod.Value.ToString("yyyy-MM-dd")));
            }
            urlset.Add(url);
        }

        AddUrl("/", "daily", "1.0");

        var categories = await _context.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        foreach (var category in categories)
        {
            AddUrl($"/Products?categoryId={category.Id}", "weekly", "0.8");
        }

        // Same "has at least one published product" rule the mega-menu/tile carousels use to
        // hide empty subcategories — an empty subcategory is exactly as much of a dead end for a
        // search crawler as it is for a shopper clicking a menu link.
        var subcategoryIdsWithPublishedProducts = (await _context.Products
            .Where(p => p.IsPublished && p.SubcategoryId != null)
            .Select(p => p.SubcategoryId!.Value)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        var subcategories = await _context.Subcategories.AsNoTracking()
            .Where(s => subcategoryIdsWithPublishedProducts.Contains(s.Id))
            .OrderBy(s => s.Id)
            .ToListAsync();
        foreach (var subcategory in subcategories)
        {
            AddUrl($"/Products?categoryId={subcategory.CategoryId}&subcategoryId={subcategory.Id}", "weekly", "0.7");
        }

        var products = await _context.Products.AsNoTracking()
            .Where(p => p.IsPublished)
            .Select(p => new { p.Slug, p.CreatedAt })
            .ToListAsync();
        foreach (var product in products)
        {
            AddUrl($"/Products/Details?slug={product.Slug}", "weekly", "0.6", product.CreatedAt);
        }

        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + urlset;
        return Content(xml, "application/xml", Encoding.UTF8);
    }

    [Route("robots.txt")]
    [OutputCache(PolicyName = "Sitemap")]
    public IActionResult Robots()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var robots = $"""
            User-agent: *
            Allow: /
            Disallow: /Admin/
            Disallow: /Identity/
            Disallow: /api/
            Disallow: /Cart
            Disallow: /Checkout

            Sitemap: {baseUrl}/sitemap.xml
            """;
        return Content(robots, "text/plain", Encoding.UTF8);
    }
}
