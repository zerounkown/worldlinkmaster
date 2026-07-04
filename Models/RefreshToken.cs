namespace WorldLinkMaster.Web.Models;

public class RefreshToken
{
    public int Id { get; set; }

    public string Token { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? CreatedByIp { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? ReplacedByToken { get; set; }

    public bool IsActive => RevokedAt == null && DateTime.UtcNow < ExpiresAt;
}
