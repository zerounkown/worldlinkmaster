namespace WorldLinkMaster.Web.Models.ViewModels;

public class CartViewModel
{
    public List<CartItem> Items { get; set; } = new();
    public decimal Subtotal => Items.Sum(i => i.LineTotal);
}
