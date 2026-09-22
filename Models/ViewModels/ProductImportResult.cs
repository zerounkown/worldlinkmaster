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

    // Non-blocking — a missing Vendor SKU/Vendor Color Code never stops an import (plenty of
    // WLM own-label products legitimately have neither), but leaving it silent is exactly how
    // the Condor/Propper catalogs drifted this far out of sync with their real vendor codes.
    public List<string> Warnings { get; set; } = new();

    public bool HasActivity =>
        ProductsCreated + ProductsUpdated + ProductColorsCreated + ProductColorsUpdated +
        VariantsCreated + VariantsUpdated + MediaWritten + AttributesWritten + Errors.Count > 0;
}
