using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

// Shared color lookup — one row per distinct color across the whole catalog, referenced by
// ProductVariant. Replaces the old per-product ProductColor list so the same "Black" (with
// the same hex) is one row instead of one row per product that happens to offer it.
public class Color
{
    public int Id { get; set; }

    // Master-data import code (e.g. "BLK"). Null for colors created before the Master Data
    // importer existed.
    [StringLength(20)]
    public string? Code { get; set; }

    [Required, StringLength(40)]
    public string Name { get; set; } = string.Empty;

    [StringLength(40)]
    public string? NameAr { get; set; }

    [Required, StringLength(7)]
    public string HexCode { get; set; } = "#000000";

    public int DisplayOrder { get; set; }

    public bool Active { get; set; } = true;
}
