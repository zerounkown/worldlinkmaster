using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class ProductSize
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Required, StringLength(20)]
    public string Label { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
