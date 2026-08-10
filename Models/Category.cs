using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class Category
{
    public int Id { get; set; }

    // Master-data import code (e.g. "CLO"). Null for categories created before the Master
    // Data importer existed.
    [StringLength(20)]
    public string? Code { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? NameAr { get; set; }

    [Required, StringLength(120)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(400)]
    public string? Description { get; set; }

    [StringLength(400)]
    public string? DescriptionAr { get; set; }

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public int DisplayOrder { get; set; }

    public bool Active { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Subcategory> Subcategories { get; set; } = new List<Subcategory>();
}
