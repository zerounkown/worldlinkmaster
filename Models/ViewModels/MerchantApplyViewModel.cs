using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models.ViewModels;

public class MerchantApplyViewModel
{
    [Required, StringLength(150)]
    [Display(Name = "Business Name")]
    public string BusinessName { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Tell customers about your shop")]
    public string? Description { get; set; }
}
