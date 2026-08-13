using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class Subcategory
{
    public int Id { get; set; }

    // Master-data import code (e.g. "CLO-TR"). Null for subcategories created before the
    // Master Data importer existed.
    [StringLength(20)]
    public string? Code { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    public string? NameAr { get; set; }

    [Required, StringLength(170)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    [StringLength(400)]
    public string? Description { get; set; }

    [StringLength(400)]
    public string? DescriptionAr { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int DisplayOrder { get; set; }

    public bool Active { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
