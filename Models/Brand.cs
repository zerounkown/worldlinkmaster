using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class Brand
{
    public int Id { get; set; }

    // Master-data import code (e.g. "WLM", "CON"). Null for brands created before the
    // Master Data importer existed.
    [StringLength(20)]
    public string? Code { get; set; }

    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [StringLength(80)]
    public string? NameAr { get; set; }

    [Required, StringLength(100)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(500)]
    public string? LogoUrl { get; set; }

    [StringLength(300)]
    public string? Website { get; set; }

    public bool Active { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
