namespace WorldLinkMaster.Web.Models.ViewModels;

public class ProductListViewModel
{
    public List<Product> Products { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public int? SelectedCategoryId { get; set; }
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}
