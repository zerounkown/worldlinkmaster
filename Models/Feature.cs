using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class Feature
{
    public int Id { get; set; }

    [Required, StringLength(60)]
    public string Name { get; set; } = string.Empty;

    [StringLength(60)]
    public string? NameAr { get; set; }

    // Bootstrap Icons class (e.g. "bi-droplet") shown next to this feature badge on the
    // Product Details page. Falls back to a generic checkmark when not set, so existing
    // Features created before this field don't need a data migration.
    [StringLength(40)]
    public string? IconClass { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
