using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

// One spec value for one product (e.g. Product #12, Attribute "Material", Value "Ripstop").
public class ProductAttributeValue
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int AttributeDefinitionId { get; set; }
    public AttributeDefinition? AttributeDefinition { get; set; }

    [Required, StringLength(250)]
    public string ValueEn { get; set; } = string.Empty;

    [StringLength(250)]
    public string? ValueAr { get; set; }

    public bool UseInFilter { get; set; }

    public int DisplayOrder { get; set; }

    public bool Active { get; set; } = true;
}
