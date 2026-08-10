using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldLinkMaster.Web.Models;

// One specific color+size combination of a product, with its own SKU and stock — replaces
// the old ProductColor/ProductSize lists (which had no notion of a combo being individually
// in or out of stock).
public class ProductVariant
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int? ColorId { get; set; }
    public Color? Color { get; set; }

    public int? SizeId { get; set; }
    public Size? Size { get; set; }

    // Set by the Product Importer, which models color as its own per-product entity
    // (ProductColor) rather than the shared Color lookup directly. ColorId above is kept in
    // sync with ProductColor.ColorId so existing code that reads Variant.Color keeps working.
    public int? ProductColorId { get; set; }
    public ProductColor? ProductColor { get; set; }

    [Required, StringLength(60)]
    public string Sku { get; set; } = string.Empty;

    [StringLength(40)]
    public string? Barcode { get; set; }

    public int StockQuantity { get; set; }

    public int? AlertQty { get; set; }

    public bool AllowBackorder { get; set; }

    public bool Active { get; set; } = true;

    // Optional per-color product shot (e.g. the "Black" swatch shows the black product photo
    // instead of the default). Mirrors what ProductColor.ImageUrl used to do.
    [StringLength(500)]
    public string? ImageUrl { get; set; }

    // لو فاضي (null)، يستخدم النظام سعر الـ Product الأساسي كـ Fallback
    [Column(TypeName = "decimal(10,2)")]
    public decimal? Price { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? WholesalePrice { get; set; }

    [Column(TypeName = "decimal(5,4)")]
    public decimal? VatRate { get; set; }
}
