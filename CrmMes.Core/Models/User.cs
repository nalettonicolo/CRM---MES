namespace CrmMes.Core.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Operator";
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Hashed short PIN for quick self-identification at the shop-floor terminal — separate
    /// from the login password, since typing a full password at a kiosk is impractical. Null until an
    /// Admin sets one for this user; a user without a PIN simply can't identify themselves there.</summary>
    public string? PinHash { get; set; }

    /// <summary>Consecutive wrong passwords; reset by a successful login. At the threshold the account is
    /// locked until <see cref="LockoutEndsAt"/>, so a public API can't be used to guess passwords at will.</summary>
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Area> Areas { get; set; } = new List<Area>();
}
