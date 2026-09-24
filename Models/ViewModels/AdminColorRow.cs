namespace WorldLinkMaster.Web.Models.ViewModels;

public class AdminColorRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string HexCode { get; set; } = "#808080";
    public int? FamilyId { get; set; }
    public string? FamilyName { get; set; }
    public int VariantProductCount { get; set; }
    public int ProductColorCount { get; set; }
}
