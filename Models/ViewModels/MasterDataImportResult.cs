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

    public bool HasActivity =>
        BrandsCreated + BrandsUpdated + CategoriesCreated + CategoriesUpdated +
        SubcategoriesCreated + SubcategoriesUpdated + ColorsCreated + ColorsUpdated +
        SizeGroupsCreated + SizeGroupsUpdated + SizesCreated + SizesUpdated +
        AttributesCreated + AttributesUpdated + Errors.Count > 0;
}
