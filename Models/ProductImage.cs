using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class ProductImage
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Required, StringLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    [StringLength(60)]
    public string? Label { get; set; }

    public int SortOrder { get; set; }
}
