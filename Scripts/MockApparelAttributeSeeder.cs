using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Scripts;

// Generates realistic-looking PLACEHOLDER data for the mega-menu's brand/attribute sub-filters
// (T-shirt family: brand, neck type, sleeve length; pants family: brand, pants type) — a stand-in
// for the real values that will eventually arrive via the Excel import, per the mega-menu
// sub-filtering request. Deliberately NOT wired into SeedData's automatic startup seed, since
// unlike AttributeDefinition rows (empty metadata, harmless), this writes real per-product data
// (BrandId reassignment + ProductAttributeValue rows) that would be visible to real shoppers.
// Only ever runs when explicitly invoked — see the /dev/mock-apparel-attrs/* endpoints in
// Program.cs (Development-only, behind a confirm=yes query flag).
//
// Brand assignment doesn't invent new brand names: it reuses ones already sitting unused in the
// Brands table, most of which are literally the first word of a product's own name (e.g.
// "Sentinel Combat Shirt" was defaulted to the house brand "WLM" on import despite the name
// clearly implying an existing "Sentinel" brand row). Only products still on that default get
// reassigned. Every reassignment is recorded in a MOCK_BRAND_ORIGINAL marker value (an internal,
// non-filterable attribute — see SeedData.SeedApparelAttributeDefinitionsAsync) holding the exact
// pre-seed BrandId, so RemoveAsync can restore precisely what it changed. This matters for a real
// edge case found while testing: a product can already legitimately carry the same brand this
// heuristic would have picked (e.g. "Condor Polo T-Shirt" was already correctly Brand=Condor on
// import) — without the marker, a naive "does the name match the current brand?" remove would
// wrongly revert that already-correct assignment. With it, remove only ever touches products it
// can prove it personally reassigned.
//
// Neck type / sleeve length / pants type are derived from the product name wherever it already
// spells the answer out (e.g. "Long Sleeve Crew Neck T-Shirt"), and fall back to a deterministic
// (never random) pick otherwise, so re-running the seed always produces the same result for the
// same product. These are strictly additive: a product that already has a value for one of these
// three attributes (mock or real) is left untouched, never overwritten. That also means: run
// RemoveAsync before a real Excel import starts populating NECK_TYPE/SLEEVE_LENGTH/PANTS_TYPE —
// once real values exist under these same codes, remove can no longer tell them apart from
// leftover mock ones and should not be run.
public static class MockApparelAttributeSeeder
{
    private const string DefaultBrandSlug = "wlm";
    private const string BrandMarkerCode = "MOCK_BRAND_ORIGINAL";
    private const string NullMarker = "(none)";

    private static readonly string[] TShirtFamilySlugs =
    {
        "t-shirts", "combat-shirts", "combat-shirt", "short-sleeve-t-shirt", "long-sleeve-t-shirt",
        "short-sleeve-crew-neck-t-shirt", "long-sleeve-crew-neck-t-shirt"
    };

    private static readonly string[] PantsFamilySlugs = { "tactical-pants", "tactical-shorts", "tactical-trousers" };

    private static readonly string[] NeckTypeFallback = { "Crew Neck", "V-Neck", "Henley", "Mock Neck" };
    private static readonly string[] PantsTypeFallback = { "Military", "Ripstop", "Cargo", "Gabardine", "Casual" };

    public record SeedResult(int ProductsScanned, int BrandsReassigned, int NeckTypeValuesSet, int SleeveLengthValuesSet, int PantsTypeValuesSet);

    public record RemoveResult(int BrandsReverted, int AttributeValuesRemoved, int MarkersRemoved);

    public static async Task<SeedResult> SeedAsync(ApplicationDbContext db)
    {
        var relevantSlugs = TShirtFamilySlugs.Concat(PantsFamilySlugs).ToArray();
        var brands = await db.Brands.ToListAsync();
        var defaultBrand = brands.FirstOrDefault(b => b.Slug == DefaultBrandSlug);

        var attrDefs = await db.AttributeDefinitions
            .Where(a => a.Code == "NECK_TYPE" || a.Code == "SLEEVE_LENGTH" || a.Code == "PANTS_TYPE" || a.Code == BrandMarkerCode)
            .ToListAsync();
        var neckTypeDef = RequireDef(attrDefs, "NECK_TYPE");
        var sleeveLengthDef = RequireDef(attrDefs, "SLEEVE_LENGTH");
        var pantsTypeDef = RequireDef(attrDefs, "PANTS_TYPE");
        var brandMarkerDef = RequireDef(attrDefs, BrandMarkerCode);

        var products = await db.Products
            .Include(p => p.Subcategory)
            .Include(p => p.AttributeValues)
            .Where(p => p.Subcategory != null && relevantSlugs.Contains(p.Subcategory.Slug))
            .ToListAsync();

        var brandsReassigned = 0;
        var neckSet = 0;
        var sleeveSet = 0;
        var pantsSet = 0;

        foreach (var product in products)
        {
            var subSlug = product.Subcategory!.Slug;
            var isTShirtFamily = TShirtFamilySlugs.Contains(subSlug);
            var isPantsFamily = PantsFamilySlugs.Contains(subSlug);
            var nameLower = product.Name.ToLowerInvariant();

            // Only ever reassign a product still sitting on the default house brand, and only
            // when there's no marker already recorded (avoids re-reassigning + re-marking on a
            // second seed run without a remove in between).
            var alreadyMarked = product.AttributeValues.Any(v => v.AttributeDefinitionId == brandMarkerDef.Id);
            if (defaultBrand != null && product.BrandId == defaultBrand.Id && !alreadyMarked)
            {
                var matchedBrand = brands
                    .Where(b => b.Id != defaultBrand.Id)
                    .FirstOrDefault(b => product.Name.StartsWith(b.Name, StringComparison.OrdinalIgnoreCase));
                if (matchedBrand != null)
                {
                    db.ProductAttributeValues.Add(new ProductAttributeValue
                    {
                        ProductId = product.Id,
                        AttributeDefinitionId = brandMarkerDef.Id,
                        ValueEn = defaultBrand.Id.ToString(),
                        UseInFilter = false,
                        Active = true
                    });
                    product.BrandId = matchedBrand.Id;
                    brandsReassigned++;
                }
            }

            if (isTShirtFamily)
            {
                var sleeveLength = nameLower.Contains("long sleeve") ? "Long Sleeve"
                    : nameLower.Contains("short sleeve") ? "Short Sleeve"
                    : "Long Sleeve"; // bare "Combat Shirt" — combat shirts are conventionally long-sleeve
                if (AddAttributeValueIfMissing(db, product, sleeveLengthDef, sleeveLength)) sleeveSet++;

                var neckType = nameLower.Contains("crew neck") ? "Crew Neck"
                    : nameLower.Contains("v-neck") || nameLower.Contains("vneck") ? "V-Neck"
                    : DeterministicPick(NeckTypeFallback, product.Id);
                if (AddAttributeValueIfMissing(db, product, neckTypeDef, neckType)) neckSet++;
            }

            if (isPantsFamily)
            {
                var pantsType = nameLower.Contains("combat") ? "Combat"
                    : nameLower.Contains("field") ? "Field"
                    : nameLower.Contains("cargo") ? "Cargo"
                    : DeterministicPick(PantsTypeFallback, product.Id);
                if (AddAttributeValueIfMissing(db, product, pantsTypeDef, pantsType)) pantsSet++;
            }
        }

        await db.SaveChangesAsync();
        return new SeedResult(products.Count, brandsReassigned, neckSet, sleeveSet, pantsSet);
    }

    public static async Task<RemoveResult> RemoveAsync(ApplicationDbContext db)
    {
        var relevantSlugs = TShirtFamilySlugs.Concat(PantsFamilySlugs).ToArray();

        var attrDefs = await db.AttributeDefinitions
            .Where(a => a.Code == "NECK_TYPE" || a.Code == "SLEEVE_LENGTH" || a.Code == "PANTS_TYPE" || a.Code == BrandMarkerCode)
            .ToListAsync();
        var brandMarkerDefId = attrDefs.FirstOrDefault(a => a.Code == BrandMarkerCode)?.Id;
        var valueDefIds = attrDefs.Where(a => a.Code != BrandMarkerCode).Select(a => a.Id).ToList();

        var productIds = await db.Products
            .Where(p => p.Subcategory != null && relevantSlugs.Contains(p.Subcategory.Slug))
            .Select(p => p.Id)
            .ToListAsync();

        var brandsReverted = 0;
        if (brandMarkerDefId.HasValue)
        {
            var markers = await db.ProductAttributeValues
                .Where(v => v.AttributeDefinitionId == brandMarkerDefId.Value && productIds.Contains(v.ProductId))
                .ToListAsync();
            if (markers.Count > 0)
            {
                var markedProductIds = markers.Select(m => m.ProductId).ToList();
                var products = await db.Products.Where(p => markedProductIds.Contains(p.Id)).ToListAsync();
                foreach (var marker in markers)
                {
                    var product = products.FirstOrDefault(p => p.Id == marker.ProductId);
                    if (product == null)
                    {
                        continue;
                    }
                    product.BrandId = marker.ValueEn == NullMarker ? null : int.Parse(marker.ValueEn);
                    brandsReverted++;
                }
                db.ProductAttributeValues.RemoveRange(markers);
            }
        }

        var valuesToRemove = await db.ProductAttributeValues
            .Where(v => valueDefIds.Contains(v.AttributeDefinitionId) && productIds.Contains(v.ProductId))
            .ToListAsync();
        db.ProductAttributeValues.RemoveRange(valuesToRemove);

        await db.SaveChangesAsync();
        return new RemoveResult(brandsReverted, valuesToRemove.Count, brandMarkerDefId.HasValue ? brandsReverted : 0);
    }

    private static bool AddAttributeValueIfMissing(ApplicationDbContext db, Product product, AttributeDefinition definition, string value)
    {
        if (product.AttributeValues.Any(v => v.AttributeDefinitionId == definition.Id))
        {
            return false;
        }

        db.ProductAttributeValues.Add(new ProductAttributeValue
        {
            ProductId = product.Id,
            AttributeDefinitionId = definition.Id,
            ValueEn = value,
            UseInFilter = true,
            Active = true
        });
        return true;
    }

    private static AttributeDefinition RequireDef(List<AttributeDefinition> defs, string code) =>
        defs.FirstOrDefault(a => a.Code == code)
            ?? throw new InvalidOperationException($"{code} attribute definition not found — run SeedApparelAttributeDefinitionsAsync first.");

    private static string DeterministicPick(string[] candidates, int seed) => candidates[seed % candidates.Length];
}
