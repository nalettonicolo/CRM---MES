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

    /// <summary>Two-factor authentication with an authenticator app (see Totp in CrmMes.Api). The secret is
    /// stored encrypted; the pending one exists only between "start setup" and the first valid code. Recovery
    /// codes are kept as hashes (';'-separated), each usable once. LastStep refuses a code used twice.</summary>
    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }
    public string? TwoFactorPendingSecret { get; set; }
    public string TwoFactorRecoveryCodes { get; set; } = string.Empty;
    public long TwoFactorLastStep { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>External identity provider key (OIDC/SSO exchange), e.g. "azure". Paired with <see cref="ExternalSubject"/>.</summary>
    public string? ExternalProvider { get; set; }
    public string? ExternalSubject { get; set; }

    public ICollection<Area> Areas { get; set; } = new List<Area>();
}
