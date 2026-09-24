using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

// Groups vendor-specific Colors ("Coyote Brown", "AR 670-1 Coyote Brown", "Coyote Tan", ...)
// into the small set of families shoppers actually filter by. The color filter on listing
// pages facets on this, not on individual Colors — PDP/product-card swatches are untouched and
// keep showing the specific vendor color name.
public class ColorFamily
{
    public int Id { get; set; }

    [Required, StringLength(30)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string Name { get; set; } = string.Empty;

    [StringLength(40)]
    public string? NameAr { get; set; }

    [Required, StringLength(7)]
    public string HexCode { get; set; } = "#808080";

    // Representative pattern image for families a flat hex can't meaningfully show (Camo).
    // Falls back to HexCode when null.
    [StringLength(500)]
    public string? SwatchImageUrl { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<Color> Colors { get; set; } = new List<Color>();
}
