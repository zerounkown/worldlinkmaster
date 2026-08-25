namespace WorldLinkMaster.Web.Models.ViewModels;

public class ProductPhotoUploadResult
{
    public List<MatchedPhoto> Matched { get; set; } = new();
    public List<UnmatchedPhoto> UnmatchedProduct { get; set; } = new();
    public List<UnmatchedPhoto> UnmatchedColor { get; set; } = new();
    public List<string> Invalid { get; set; } = new();

    public bool HasActivity => Matched.Count + UnmatchedProduct.Count + UnmatchedColor.Count + Invalid.Count > 0;

    public record MatchedPhoto(string FileName, string ProductName, string ProductCode, string ColorName, string MediaUrl, bool Replaced);
    public record UnmatchedPhoto(string FileName, string VendorSku, string VendorColorCode, string? ProductName, string? KnownColors);
}
