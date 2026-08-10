using System.Globalization;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.Web.Extensions;

// Customer-entered content (product/category/subcategory names & descriptions) lives in the
// database, not in the .resx UI strings, so it needs its own Arabic fallback logic — these
// mirror the *Ar columns added alongside the English ones and fall back to English whenever
// a translation hasn't been filled in yet.
public static class LocalizedContentExtensions
{
    private static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public static string LocalizedName(this Product product) =>
        IsArabic && !string.IsNullOrWhiteSpace(product.NameAr) ? product.NameAr! : product.Name;

    public static string? LocalizedShortDescription(this Product product) =>
        IsArabic && !string.IsNullOrWhiteSpace(product.ShortDescriptionAr) ? product.ShortDescriptionAr : product.ShortDescription;

    public static string? LocalizedDescription(this Product product) =>
        IsArabic && !string.IsNullOrWhiteSpace(product.DescriptionAr) ? product.DescriptionAr : product.Description;

    public static string? LocalizedOverview(this Product product) =>
        IsArabic && !string.IsNullOrWhiteSpace(product.OverviewAr) ? product.OverviewAr : product.Overview;

    public static string LocalizedName(this Category category) =>
        IsArabic && !string.IsNullOrWhiteSpace(category.NameAr) ? category.NameAr! : category.Name;

    public static string? LocalizedDescription(this Category category) =>
        IsArabic && !string.IsNullOrWhiteSpace(category.DescriptionAr) ? category.DescriptionAr : category.Description;

    public static string LocalizedName(this Subcategory subcategory) =>
        IsArabic && !string.IsNullOrWhiteSpace(subcategory.NameAr) ? subcategory.NameAr! : subcategory.Name;

    public static string LocalizedName(this Brand brand) =>
        IsArabic && !string.IsNullOrWhiteSpace(brand.NameAr) ? brand.NameAr! : brand.Name;

    public static string LocalizedName(this Feature feature) =>
        IsArabic && !string.IsNullOrWhiteSpace(feature.NameAr) ? feature.NameAr! : feature.Name;

    public static string LocalizedName(this FacetCount facet) =>
        IsArabic && !string.IsNullOrWhiteSpace(facet.NameAr) ? facet.NameAr! : facet.Name;

    public static string LocalizedTitle(this HomeBanner banner) =>
        IsArabic && !string.IsNullOrWhiteSpace(banner.TitleAr) ? banner.TitleAr! : banner.TitleEn;

    public static string? LocalizedSubtitle(this HomeBanner banner) =>
        IsArabic && !string.IsNullOrWhiteSpace(banner.SubtitleAr) ? banner.SubtitleAr : banner.SubtitleEn;
}
