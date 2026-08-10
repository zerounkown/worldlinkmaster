using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

// A named spec field a product can carry a value for (e.g. "Material", "Capacity"). Named
// "AttributeDefinition" rather than "Attribute" to avoid colliding with System.Attribute.
public class AttributeDefinition
{
    public int Id { get; set; }

    [Required, StringLength(40)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(80)]
    public string? NameAr { get; set; }

    [StringLength(20)]
    public string? DataType { get; set; }

    public bool Filterable { get; set; }

    public bool Active { get; set; } = true;
}
