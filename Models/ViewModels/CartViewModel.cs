namespace WorldLinkMaster.Web.Models.ViewModels;

public class CartViewModel
{
    public List<CartItem> Items { get; set; } = new();
    public decimal Subtotal => Items.Sum(i => i.LineTotal);

    public const decimal FreeShippingThreshold = 365m;
    public decimal ShippingCost => Subtotal >= FreeShippingThreshold || Subtotal == 0 ? 0m : 36.99m;
    public decimal AmountAwayFromFreeShipping => Math.Max(0, FreeShippingThreshold - Subtotal);
    public bool QualifiesForFreeShipping => Subtotal >= FreeShippingThreshold;

    public string? AppliedCouponCode { get; set; }
    public decimal CouponDiscountPercent { get; set; }
    public decimal CouponDiscountAmount => Math.Round(Subtotal * CouponDiscountPercent / 100m, 2);

    public decimal Total => Subtotal - CouponDiscountAmount + ShippingCost;

    // Per-line enrichment not stored on CartItem itself (looked up from ProductVariant)
    public Dictionary<string, CartLineStockInfo> StockInfoByLineKey { get; set; } = new();
}

public class CartLineStockInfo
{
    public string? Sku { get; set; }
    public int StockQuantity { get; set; }
}
