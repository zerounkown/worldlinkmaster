using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models.ViewModels;

public class CustomerListItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public IList<string> Roles { get; set; } = new List<string>();
    public bool LockedOut { get; set; }
    public bool EmailConfirmed { get; set; }
    public int OrderCount { get; set; }
}

public class CustomerDetailsViewModel
{
    public ApplicationUser User { get; set; } = null!;
    public IList<string> Roles { get; set; } = new List<string>();
    public bool LockedOut { get; set; }
    public List<Order> Orders { get; set; } = new();
    public Merchant? MerchantProfile { get; set; }
    public WholesaleAccount? WholesaleAccount { get; set; }
    public int CouponCount { get; set; }
}

public class CustomerEditViewModel
{
    public string Id { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [StringLength(100)]
    public string? FirstName { get; set; }

    [StringLength(100)]
    public string? LastName { get; set; }

    [Phone]
    public string? PhoneNumber { get; set; }

    public bool EmailConfirmed { get; set; }

    public bool LockedOut { get; set; }

    public bool IsAdmin { get; set; }
    public bool IsMerchant { get; set; }
    public bool IsWholesale { get; set; }
    public bool IsCustomer { get; set; }
    public bool IsSupport { get; set; }
}
