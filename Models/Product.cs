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

    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    // Special trade/wholesale price for approved business buyers. Null means this
    // product isn't part of the wholesale program and only sells at retail Price.
    [Column(TypeName = "decimal(10,2)")]
    public decimal? WholesalePrice { get; set; }

    [Required, StringLength(40)]
    public string Sku { get; set; } = string.Empty;

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

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductColor> Colors { get; set; } = new List<ProductColor>();
    public ICollection<ProductSize> Sizes { get; set; } = new List<ProductSize>();
}
