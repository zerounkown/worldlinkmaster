namespace WorldLinkMaster.Web.Models.ViewModels;

public class MasterDataImportResult
{
    public int BrandsCreated { get; set; }
    public int BrandsUpdated { get; set; }
    public int CategoriesCreated { get; set; }
    public int CategoriesUpdated { get; set; }
    public int SubcategoriesCreated { get; set; }
    public int SubcategoriesUpdated { get; set; }
    public int ColorsCreated { get; set; }
    public int ColorsUpdated { get; set; }
    public int SizeGroupsCreated { get; set; }
    public int SizeGroupsUpdated { get; set; }
    public int SizesCreated { get; set; }
    public int SizesUpdated { get; set; }
    public int AttributesCreated { get; set; }
    public int AttributesUpdated { get; set; }

    public List<string> Errors { get; set; } = new();

    // Set only when an unexpected exception aborted the import outright (a DB constraint
    // violation not caught by per-row validation, a connectivity failure, etc.) — a per-row
    // problem always goes in Errors instead and just skips that row. Whichever sheets had already
    // called SaveChangesAsync before the failure are NOT rolled back (each sheet commits
    // independently — see MasterDataController), so the counts above can be non-zero even when
    // this is set.
    public string? FatalError { get; set; }

    public bool HasActivity =>
        BrandsCreated + BrandsUpdated + CategoriesCreated + CategoriesUpdated +
        SubcategoriesCreated + SubcategoriesUpdated + ColorsCreated + ColorsUpdated +
        SizeGroupsCreated + SizeGroupsUpdated + SizesCreated + SizesUpdated +
        AttributesCreated + AttributesUpdated + Errors.Count > 0;
}
