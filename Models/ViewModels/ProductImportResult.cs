namespace WorldLinkMaster.Web.Models.ViewModels;

public class ProductImportResult
{
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public int ProductColorsCreated { get; set; }
    public int ProductColorsUpdated { get; set; }
    public int VariantsCreated { get; set; }
    public int VariantsUpdated { get; set; }

    // Media and Attributes are replace-per-product on every import (no stable row key in the
    // sheet to match against for an update), so only a "written" count makes sense for them.
    public int MediaWritten { get; set; }
    public int AttributesWritten { get; set; }

    public List<string> Errors { get; set; } = new();

    public bool HasActivity =>
        ProductsCreated + ProductsUpdated + ProductColorsCreated + ProductColorsUpdated +
        VariantsCreated + VariantsUpdated + MediaWritten + AttributesWritten + Errors.Count > 0;
}
