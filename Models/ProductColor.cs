using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

// One color option a specific product is offered in (e.g. "WLM-TR-001-BLK") — distinct from
// the shared Color lookup, since the same Color can be a different swatch/default per product.
// Variants and color-scoped Media both hang off this, not off Color directly.
public class ProductColor
{
    public int Id { get; set; }

    [Required, StringLength(60)]
    public string Code { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int ColorId { get; set; }
    public Color? Color { get; set; }

    public int DisplayOrder { get; set; }

    public bool DefaultColor { get; set; }

    [StringLength(500)]
    public string? SwatchImageUrl { get; set; }

    public bool Active { get; set; } = true;

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductMedia> Media { get; set; } = new List<ProductMedia>();
}
