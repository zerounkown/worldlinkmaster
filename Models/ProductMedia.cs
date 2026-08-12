using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

// One image or video row for a product — either shared across every color (ProductColorId
// null, MediaScope "Shared") or specific to one color option (MediaScope "Color"). See the
// Media Rules (M001-M009) in WLM_01_Developer_Specification.xlsx for the intended gallery
// behavior this data drives (not yet wired into the storefront — import-side only for now).
public class ProductMedia
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    // Null when MediaScope is "Shared".
    public int? ProductColorId { get; set; }
    public ProductColor? ProductColor { get; set; }

    [Required, StringLength(10)]
    public string MediaScope { get; set; } = "Shared"; // Shared | Color

    [Required, StringLength(10)]
    public string MediaType { get; set; } = "Image"; // Image | Video

    [StringLength(40)]
    public string? MediaRole { get; set; }

    [Required, StringLength(500)]
    public string MediaUrl { get; set; } = string.Empty;

    [StringLength(500)]
    public string? ThumbnailUrl { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsColorMain { get; set; }

    public bool ShowInGallery { get; set; } = true;

    [StringLength(250)]
    public string? AltTextEn { get; set; }

    [StringLength(250)]
    public string? AltTextAr { get; set; }

    public bool Active { get; set; } = true;
}
