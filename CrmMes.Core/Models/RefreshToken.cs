namespace CrmMes.Core.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>Channel the session was opened from (desktop, web, mobile): a renewal re-checks that the
    /// role may still use it, so an Admin's change takes effect within one access-token lifetime.</summary>
    public string Channel { get; set; } = "desktop";
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
