using Microsoft.AspNetCore.Identity;

namespace WorldLinkMaster.Web.Models;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Full public URL of the avatar blob (Azure Blob Storage, "avatars" container).
    /// Null means the user has no profile picture and the default icon is shown instead.</summary>
    public string? ProfilePictureUrl { get; set; }
}
