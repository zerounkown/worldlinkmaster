using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class ProductColor
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Required, StringLength(40)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(7)]
    public string HexCode { get; set; } = "#000000";

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public int SortOrder { get; set; }
}
