using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Tests.IntegrationTests;

/// <summary>
/// Controllers/ProductsController.cs InstantSearch — the header dropdown's one lightweight
/// endpoint. Runs the real HTTP pipeline against the SQLite-backed CustomWebApplicationFactory
/// (not just the Postgres/production path), since the action deliberately has two separate
/// query-building branches (EF.Functions.ILike for Npgsql vs ToLower().Contains() for SQLite) —
/// this class is what actually exercises the SQLite branch, matching the task's "must work on
/// PostgreSQL and in the SQLite unit tests" requirement.
/// </summary>
public class ProductsInstantSearchTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsInstantSearchTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        Seed();
    }

    private void Seed()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (context.Products.Any(p => p.Slug == "instant-search-shirt"))
        {
            return; // Already seeded by a previous test in this class (shared factory/DB).
        }

        var category = new Category { Id = 9101, Name = "Instant Search Category", Slug = "instant-search-category" };
        context.Categories.Add(category);

        var publishedProduct = new Product
        {
            Id = 9101,
            Name = "Trailblazer Base Layer",
            // A stray U+200E (LEFT-TO-RIGHT MARK) sitting right before "24-7" — the real-world
            // data issue reported for some imported Arabic names. A search for "24-7" must still
            // match this, not silently fail because the invisible character splits the substring.
            NameAr = "‎طبقة أساس تريلبليزر 24-7",
            Slug = "instant-search-shirt",
            Sku = "WLM-TBL-001",
            Price = 180m,
            StockQuantity = 10,
            CategoryId = category.Id,
            MerchantId = 1,
            IsPublished = true
        };
        context.Products.Add(publishedProduct);

        var black = new Color { Id = 9101, Name = "Black", HexCode = "#000000" };
        context.Colors.Add(black);

        context.ProductVariants.Add(new ProductVariant
        {
            ProductId = publishedProduct.Id,
            Sku = "WLM-TBL-001-BLK",
            Barcode = "6291234567890",
            ColorId = black.Id,
            StockQuantity = 5,
            Active = true
        });

        var unpublishedProduct = new Product
        {
            Id = 9102,
            Name = "Unpublished Draft Jacket",
            Slug = "instant-search-draft",
            Sku = "WLM-DRAFT-002",
            Price = 300m,
            StockQuantity = 0,
            CategoryId = category.Id,
            MerchantId = 1,
            IsPublished = false
        };
        context.Products.Add(unpublishedProduct);

        context.SaveChanges();
    }

    // A cookie-less request defaults to Arabic (Program.cs — see Tests.E2E/Infrastructure/
    // E2ETestBase.cs's own note on this), which would make the "name" field unpredictable for
    // tests that don't care about localization. Sets the same ".AspNetCore.Culture" cookie
    // format the real language switch writes (LocalizationController.SetLanguage) so each test
    // is explicit about which UI language it's asserting against.
    private async Task<JsonElement> SearchAsync(string term, string culture = "en")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $".AspNetCore.Culture=c={culture}|uic={culture}");
        var response = await client.GetAsync("/Products/InstantSearch?q=" + Uri.EscapeDataString(term));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static List<JsonElement> ResultsOf(JsonElement json) =>
        json.GetProperty("results").EnumerateArray().ToList();

    [Fact]
    public async Task Search_ByEnglishName_ReturnsMatch()
    {
        var results = ResultsOf(await SearchAsync("Trailblazer"));

        Assert.Single(results);
        Assert.Equal("Trailblazer Base Layer", results[0].GetProperty("name").GetString());
        // Matched via the product's own Name — "Trailblazer" isn't a substring of the variant's
        // Sku/Barcode, so no variant matched and there's no color to deep-link to. The URL
        // carries no "color" query parameter at all (not an empty one).
        Assert.DoesNotContain("color=", results[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Search_ByEnglishName_IsCaseInsensitive()
    {
        var results = ResultsOf(await SearchAsync("trailblazer base"));

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_ByArabicName_MatchesDespiteLeftToRightMark()
    {
        var results = ResultsOf(await SearchAsync("24-7"));

        Assert.Single(results);
        Assert.Contains("instant-search-shirt", results[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Search_InArabicUi_ReturnsArabicName()
    {
        var results = ResultsOf(await SearchAsync("24-7", culture: "ar"));

        Assert.Single(results);
        Assert.Contains("24-7", results[0].GetProperty("name").GetString());
        Assert.DoesNotContain("Trailblazer", results[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Search_ByProductSku_ReturnsMatch()
    {
        var results = ResultsOf(await SearchAsync("WLM-TBL-001"));

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_ByVariantSku_ReturnsParentProduct_WithItsColorPreselected()
    {
        var results = ResultsOf(await SearchAsync("TBL-001-BLK"));

        Assert.Single(results);
        Assert.Equal("Trailblazer Base Layer", results[0].GetProperty("name").GetString());
        Assert.Contains("color=Black", results[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Search_ByVariantBarcode_ReturnsParentProduct_WithItsColorPreselected()
    {
        var results = ResultsOf(await SearchAsync("6291234567890"));

        Assert.Single(results);
        Assert.Contains("color=Black", results[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Search_ExcludesUnpublishedProducts()
    {
        var results = ResultsOf(await SearchAsync("Draft Jacket"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_ExcludesUnpublishedProducts_EvenByItsSku()
    {
        var results = ResultsOf(await SearchAsync("WLM-DRAFT-002"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_TermUnderTwoCharacters_ReturnsNoResultsWithoutError()
    {
        var results = ResultsOf(await SearchAsync("a"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmptyResultsNotError()
    {
        var results = ResultsOf(await SearchAsync("zzz-no-such-product-zzz"));

        Assert.Empty(results);
    }
}
