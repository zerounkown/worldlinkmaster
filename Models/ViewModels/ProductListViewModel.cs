namespace WorldLinkMaster.Web.Models.ViewModels;

public class FacetCount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int Count { get; set; }
}

// Individual vendor colors are grouped into families for the listing-page filter (product
// pages/cards still show the specific color name — this is filter-only).
public class ColorFamilyFacetCount
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string HexCode { get; set; } = "#808080";
    public string? SwatchImageUrl { get; set; }
    public int Count { get; set; }
}

public class LabelFacetCount
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ProductListViewModel
{
    public List<Product> Products { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Brand> Brands { get; set; } = new();
    public int? SelectedCategoryId { get; set; }
    public List<int> SelectedSubcategoryIds { get; set; } = new();
    public List<int> SelectedBrandIds { get; set; } = new();
    // Family codes (e.g. "black", "tan-coyote"), not individual color names.
    public List<string> SelectedColorFamilies { get; set; } = new();
    public List<string> SelectedSizes { get; set; } = new();
    public List<int> SelectedFeatureIds { get; set; } = new();
    public List<string> SelectedAvailability { get; set; } = new();
    public int? MinRating { get; set; }
    public string? SearchTerm { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int PriceRangeMin { get; set; }
    public int PriceRangeMax { get; set; }
    public string? SortBy { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }

    public List<FacetCount> SubcategoryFacets { get; set; } = new();
    public List<FacetCount> BrandFacets { get; set; } = new();
    public List<ColorFamilyFacetCount> ColorFamilyFacets { get; set; } = new();
    public List<LabelFacetCount> SizeFacets { get; set; } = new();
    public List<FacetCount> FeatureFacets { get; set; } = new();
    public int InStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public Dictionary<int, int> RatingCounts { get; set; } = new();

    public bool HasActiveFilters =>
        SelectedSubcategoryIds.Count > 0 || SelectedBrandIds.Count > 0 || SelectedColorFamilies.Count > 0 ||
        SelectedSizes.Count > 0 || SelectedFeatureIds.Count > 0 || SelectedAvailability.Count > 0 ||
        MinRating.HasValue || MinPrice.HasValue || MaxPrice.HasValue;
}
