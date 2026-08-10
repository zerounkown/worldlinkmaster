using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

// Admin-managed marketing banners shown as a carousel at the very top of the homepage
// (above the text hero) — e.g. seasonal sale promos, new-arrival spotlights.
public class HomeBanner
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string TitleEn { get; set; } = string.Empty;

    [StringLength(120)]
    public string? TitleAr { get; set; }

    [StringLength(300)]
    public string? SubtitleEn { get; set; }

    [StringLength(300)]
    public string? SubtitleAr { get; set; }

    [Required, StringLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    // Where the banner links when clicked — a relative path (e.g. "/Products?categoryId=3")
    // or a full URL. Optional: a banner with no link just isn't clickable.
    [StringLength(500)]
    public string? LinkUrl { get; set; }

    public int DisplayOrder { get; set; }

    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
