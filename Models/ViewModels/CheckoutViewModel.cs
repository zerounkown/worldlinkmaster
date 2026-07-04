using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models.ViewModels;

public class CheckoutViewModel
{
    [Required, StringLength(150)]
    [Display(Name = "Full Name")]
    public string ShippingName { get; set; } = string.Empty;

    [Required, StringLength(250)]
    [Display(Name = "Street Address")]
    public string ShippingAddress { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "City")]
    public string ShippingCity { get; set; } = string.Empty;

    [Required, StringLength(60)]
    [Display(Name = "State")]
    public string ShippingState { get; set; } = string.Empty;

    [Required, StringLength(20)]
    [Display(Name = "ZIP Code")]
    public string ShippingZip { get; set; } = string.Empty;

    [Required, StringLength(30)]
    [Display(Name = "Phone")]
    public string ShippingPhone { get; set; } = string.Empty;

    public List<CartItem> Items { get; set; } = new();
    public decimal Subtotal => Items.Sum(i => i.LineTotal);
    public decimal ShippingCost => Subtotal >= 365m || Subtotal == 0 ? 0m : 36.99m;
    public decimal Total => Subtotal + ShippingCost;
}
