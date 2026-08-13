using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldLinkMaster.Web.Models;

public enum StoreCategory
{
    OfficialStore,
    AuthorizedDealer
}

// Physical showrooms/dealers shown on the public "Find a Store" map page
// (Views/Home/Locations.cshtml) and managed from Areas/Admin/Controllers/StoresController.cs.
public class Store
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public StoreCategory Category { get; set; } = StoreCategory.OfficialStore;

    [Required, StringLength(300)]
    public string Address { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    // Digits only (e.g. "971564450046"), same convention as the WhatsAppBranches config it replaces.
    [StringLength(30)]
    public string? WhatsAppNumber { get; set; }

    [StringLength(500)]
    public string? Website { get; set; }

    [Column(TypeName = "decimal(9,6)")]
    public decimal Latitude { get; set; }

    [Column(TypeName = "decimal(9,6)")]
    public decimal Longitude { get; set; }

    public int DisplayOrder { get; set; }

    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
