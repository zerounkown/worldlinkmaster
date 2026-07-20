using Microsoft.AspNetCore.Identity;

namespace WorldLinkMaster.Web.Models;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>File name only (e.g. "3f2a...c1.jpg"), stored under wwwroot/uploads/avatars/.
    /// Null means the user has no profile picture and the default icon is shown instead.</summary>
    public string? ProfilePictureFileName { get; set; }
}
