using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class Subcategory
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    public string? NameAr { get; set; }

    [Required, StringLength(170)]
    public string Slug { get; set; } = string.Empty;

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
