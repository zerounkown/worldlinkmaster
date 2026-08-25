using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldLinkMaster.Web.Models;

public class Product
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    public string? NameAr { get; set; }

    [Required, StringLength(170)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(250)]
    public string? ShortDescription { get; set; }

    [StringLength(250)]
    public string? ShortDescriptionAr { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(2000)]
    public string? DescriptionAr { get; set; }

    // Longer-form "Overview" section shown separately from Description (own page section,
    // own accent header) — a distinct field since it's genuinely different marketing copy,
    // not a duplicate of the Description tab's intro/materials paragraphs.
    [StringLength(4000)]
    public string? Overview { get; set; }

    [StringLength(4000)]
    public string? OverviewAr { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    // Special trade/wholesale price for approved business buyers. Null means this
    // product isn't part of the wholesale program and only sells at retail Price.
    [Column(TypeName = "decimal(10,2)")]
    public decimal? WholesalePrice { get; set; }

    // VAT rate as a fraction (e.g. 0.05 for 5%) — set by the Product Importer; existing
    // products created before it have this null and are treated as VAT-unaware, as before.
    [Column(TypeName = "decimal(5,4)")]
    public decimal? VatRate { get; set; }

    [Required, StringLength(40)]
    public string Sku { get; set; } = string.Empty;

    // Manufacturer's own style number (e.g. Condor's "101228"), distinct from our internal Sku
    // (e.g. "CON-0001"). Used to match incoming product photos, which vendors name by their own
    // SKU-color code rather than ours. Null for products with no known vendor SKU.
    [StringLength(40)]
    public string? VendorSku { get; set; }

    public int StockQuantity { get; set; }

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public bool IsFeatured { get; set; }

    [Column(TypeName = "decimal(2,1)")]
    public decimal Rating { get; set; }

    public int ReviewCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? SubcategoryId { get; set; }
    public Subcategory? Subcategory { get; set; }

    public int MerchantId { get; set; }
    public Merchant? Merchant { get; set; }

    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    // Restricts which Sizes this product's Variants may use. Null for products created
    // before the Product Importer existed — those aren't restricted to any group.
    public int? SizeGroupId { get; set; }
    public SizeGroup? SizeGroup { get; set; }

    public bool IsPublished { get; set; } = true;

    [StringLength(200)]
    public string? SeoTitleEn { get; set; }

    [StringLength(200)]
    public string? SeoTitleAr { get; set; }

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<Feature> Features { get; set; } = new List<Feature>();
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductColor> ProductColors { get; set; } = new List<ProductColor>();
    public ICollection<ProductMedia> Media { get; set; } = new List<ProductMedia>();
    public ICollection<ProductAttributeValue> AttributeValues { get; set; } = new List<ProductAttributeValue>();
}
