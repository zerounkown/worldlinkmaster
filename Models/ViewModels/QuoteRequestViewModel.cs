using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models.ViewModels;

public class QuoteRequestViewModel
{
    [Required, StringLength(200)]
    [Display(Name = "Company Name")]
    public string CompanyName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required, Phone, StringLength(30)]
    [Display(Name = "Phone")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "Trade License Number (optional)")]
    public string? TradeLicenseNumber { get; set; }

    [Required, StringLength(2000)]
    [Display(Name = "Product / Service Details")]
    public string ProductDetails { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
    [Display(Name = "Quantity")]
    public int Quantity { get; set; } = 1;
}
