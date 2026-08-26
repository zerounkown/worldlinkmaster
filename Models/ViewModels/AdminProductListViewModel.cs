namespace WorldLinkMaster.Web.Models.ViewModels;

public class AdminProductListViewModel
{
    public List<Product> Products { get; set; } = new();
    public string? SearchTerm { get; set; }

    // "draft" | "published" | "all" — defaults to "draft" server-side (this list exists mainly
    // for the Condor-import cleanup work, where surfacing unpublished products is the point).
    public string StatusFilter { get; set; } = "draft";

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
}
