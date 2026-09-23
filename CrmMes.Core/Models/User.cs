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
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Area> Areas { get; set; } = new List<Area>();
}
