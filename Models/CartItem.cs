namespace WorldLinkMaster.Web.Models;

public class CartItem
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public int MerchantId { get; set; }

    public decimal LineTotal => UnitPrice * Quantity;
}
