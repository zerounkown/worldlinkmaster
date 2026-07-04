using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models.ViewModels;

public class WholesaleApplyViewModel
{
    [Required, StringLength(150)]
    [Display(Name = "Company Name")]
    public string CompanyName { get; set; } = string.Empty;

    [StringLength(60)]
    [Display(Name = "Trade License Number (optional)")]
    public string? TradeLicenseNumber { get; set; }
}
