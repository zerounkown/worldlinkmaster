namespace WorldLinkMaster.Web.Models.ViewModels;

// Flattened, display-ready shape for a review in the PDP Reviews tab — the reviewer's name is
// composed once here (FirstName/LastName with a UserName fallback) so the view doesn't need to
// know about ApplicationUser's nullable name fields.
public class ProductReviewDisplay
{
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
}
