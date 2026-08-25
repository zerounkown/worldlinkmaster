using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Extensions;

public static class ProfilePictureExtensions
{
    public static string? AvatarUrl(this ApplicationUser? user)
    {
        return string.IsNullOrEmpty(user?.ProfilePictureUrl)
            ? null
            : user.ProfilePictureUrl;
    }
}
