using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldLinkMaster.Web.Models;

// Shared size lookup — one row per distinct size label across the whole catalog (clothing
// sizes like "M", bag capacities like "45L"), referenced by ProductVariant.
public class Size
{
    public int Id { get; set; }

    // Master-data import code (e.g. "CL-M", "TR-32"). Null for sizes created before the
    // Master Data importer existed.
    [StringLength(20)]
    public string? Code { get; set; }

    [Required, StringLength(20)]
    public string Label { get; set; } = string.Empty;

    [StringLength(20)]
    public string? LabelAr { get; set; }

    public int SortOrder { get; set; }

    // Which family of sizes this belongs to (clothing letters, trouser waist, footwear EU, …).
    // Null for sizes created before the Master Data importer existed — those aren't
    // restricted to any group.
    public int? SizeGroupId { get; set; }
    public SizeGroup? SizeGroup { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? NumericValue { get; set; }

    [StringLength(20)]
    public string? Unit { get; set; }

    public bool Active { get; set; } = true;
}
