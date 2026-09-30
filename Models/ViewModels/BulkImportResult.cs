namespace WorldLinkMaster.Web.Models.ViewModels;

public class BulkImportResult
{
    public int UpdatedCount { get; set; }
    public int CreatedCount { get; set; }
    public int VariantsUpdatedCount { get; set; }
    public int VariantsCreatedCount { get; set; }

    // ProductColor rows backfilled from variants that had a color but no ProductColor row of
    // their own — see ProductsController.BackfillProductColorsFromVariants. Can be non-zero even
    // for a file whose rows don't mention the affected product at all, since this runs against
    // every color-bearing variant already tracked this request, not just ones this upload touched.
    public int ProductColorsCreated { get; set; }

    public List<string> Errors { get; set; } = new();

    // Set only when the whole import was rolled back (an unexpected exception, not a per-row
    // validation problem — those go in Errors and the row is just skipped). When this is set,
    // the counts above are always 0: the import is all-or-nothing, so nothing was saved.
    public string? FatalError { get; set; }
}
