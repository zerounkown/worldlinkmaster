using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public interface IJwtService
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(ApplicationUser user, IList<string> roles);

    RefreshToken GenerateRefreshToken(string userId, string? createdByIp);
}
