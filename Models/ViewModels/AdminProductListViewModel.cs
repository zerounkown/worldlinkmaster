namespace WorldLinkMaster.Web.Models.ViewModels;

public class AdminProductListViewModel
{
    public List<Product> Products { get; set; } = new();
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
}
