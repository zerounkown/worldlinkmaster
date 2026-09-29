using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Tests.IntegrationTests;

/// <summary>
/// Controllers/ProductsController.cs Index — the exact-match short-circuit: a search term that
/// exactly (case-insensitively) matches a variant's Sku or Barcode redirects straight to that
/// variant's product page instead of rendering the search-results page. This runs BEFORE
/// Index's price-range facet query (the one that computes bounds via MinAsync()/MaxAsync() over
/// the decimal Price column — see ProductsControllerTests.IndexSkipReason), so unlike every
/// other Index test in this suite, these don't need to be skipped on SQLite: a redirect never
/// reaches that code path at all.
/// </summary>
public class ProductsExactMatchRedirectTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsExactMatchRedirectTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        Seed();
    }

    private void Seed()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (context.Products.Any(p => p.Slug == "exact-match-shirt"))
        {
            return; // Already seeded by a previous test in this class (shared factory/DB).
        }

        var category = new Category { Id = 9201, Name = "Exact Match Category", Slug = "exact-match-category" };
        context.Categories.Add(category);

        var publishedProduct = new Product
        {
            Id = 9201,
            Name = "Exact Match Shirt",
            Slug = "exact-match-shirt",
            Sku = "WLM-EXACT-001",
            Price = 150m,
            StockQuantity = 10,
            CategoryId = category.Id,
            MerchantId = 1,
            IsPublished = true
        };
        context.Products.Add(publishedProduct);

        context.ProductVariants.Add(new ProductVariant
        {
            Id = 9201,
            ProductId = publishedProduct.Id,
            Sku = "WLM-EXACT-001-BLK",
            Barcode = "9998887776665",
            StockQuantity = 5,
            Active = true
        });

        var unpublishedProduct = new Product
        {
            Id = 9202,
            Name = "Exact Match Draft",
            Slug = "exact-match-draft",
            Sku = "WLM-EXACT-DRAFT",
            Price = 150m,
            StockQuantity = 0,
            CategoryId = category.Id,
            MerchantId = 1,
            IsPublished = false
        };
        context.Products.Add(unpublishedProduct);

        context.ProductVariants.Add(new ProductVariant
        {
            Id = 9202,
            ProductId = unpublishedProduct.Id,
            Sku = "WLM-EXACT-DRAFT-BLK",
            Barcode = "1112223334445",
            StockQuantity = 5,
            Active = true
        });

        context.SaveChanges();
    }

    private static HttpClient NoRedirectClient(CustomWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Search_ExactVariantSku_RedirectsToProductPageWithVariant()
    {
        var client = NoRedirectClient(_factory);

        var response = await client.GetAsync("/Products?search=WLM-EXACT-001-BLK");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/Products/Details", location);
        Assert.Contains("slug=exact-match-shirt", location);
        Assert.Contains("variant=9201", location);
    }

    [Fact]
    public async Task Search_ExactVariantBarcode_RedirectsToProductPageWithVariant()
    {
        var client = NoRedirectClient(_factory);

        var response = await client.GetAsync("/Products?search=9998887776665");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("slug=exact-match-shirt", location);
        Assert.Contains("variant=9201", location);
    }

    [Fact]
    public async Task Search_ExactVariantSku_IsCaseInsensitive()
    {
        var client = NoRedirectClient(_factory);

        var response = await client.GetAsync("/Products?search=wlm-exact-001-blk");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("variant=9201", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Search_ExactVariantMatch_OnUnpublishedProduct_DoesNotRedirect()
    {
        var client = NoRedirectClient(_factory);

        var response = await client.GetAsync("/Products?search=1112223334445");

        // Not asserting 200 OK here on purpose: falling through to the rest of Index() (its
        // price-range facet query, unrelated to this change) hits the same pre-existing SQLite
        // decimal MinAsync()/MaxAsync() translation gap ProductsControllerTests.IndexSkipReason
        // documents, which the Development-environment host surfaces as a 500 rather than a
        // clean 200 here. What this test actually verifies is narrower and unaffected by that:
        // FindExactVariantMatchAsync's IsPublished filter must keep the unpublished product's
        // variant from ever reaching the redirect branch at all — if it didn't, this would see a
        // 302 instead, which is the one outcome that's wrong regardless of the SQLite quirk.
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }
}
