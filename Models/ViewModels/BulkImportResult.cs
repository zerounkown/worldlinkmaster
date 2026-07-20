namespace WorldLinkMaster.Web.Models.ViewModels;

public class BulkImportResult
{
    public int UpdatedCount { get; set; }
    public int CreatedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
