using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

// A family of related sizes (e.g. clothing letter sizes, trouser waist sizes, footwear EU
// sizes) — lets a Product restrict which Sizes are valid for its Variants.
public class SizeGroup
{
    public int Id { get; set; }

    [Required, StringLength(30)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(80)]
    public string? NameAr { get; set; }

    [StringLength(20)]
    public string? UnitType { get; set; }

    [StringLength(250)]
    public string? Notes { get; set; }

    public bool Active { get; set; } = true;

    public ICollection<Size> Sizes { get; set; } = new List<Size>();
}
