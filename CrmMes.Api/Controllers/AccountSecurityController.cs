using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace CrmMes.Api.Controllers;

/// <summary>Two-factor authentication of the logged-in user (setup with an authenticator app, recovery
/// codes, switching it off), the Admin's reset for a lost phone, and which roles must use it. Optional for
/// everyone, compulsory only for the roles the Admin chooses.</summary>
[ApiController]
[Authorize]
public class AccountSecurityController : ControllerBase
{
    public const string Issuer = "Nicolò MES";
    private readonly ApplicationDbContext _dbContext;
    private readonly SecretProtector _protector;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AccountSecurityController(ApplicationDbContext dbContext, SecretProtector protector, IPasswordHasher<User> passwordHasher)
    {
        _dbContext = dbContext;
        _protector = protector;
        _passwordHasher = passwordHasher;
    }

    [HttpGet("api/account/2fa")]
    public async Task<ActionResult<TwoFactorStatusResponse>> Status(CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new TwoFactorStatusResponse(user.TwoFactorEnabled, await IsRequiredAsync(user.Role, cancellationToken),
            RecoveryHashes(user).Count));
    }

    /// <summary>Starts the setup: a new secret, shown as a QR code for the app (and as text to type). It
    /// becomes active only after Enable proves the app produces the right codes.</summary>
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("api/account/2fa/setup")]
    public async Task<ActionResult<TwoFactorSetupResponse>> Setup(CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (user.TwoFactorEnabled)
        {
            return Conflict(new { message = "La verifica in due passaggi è già attiva: disattivala prima di configurarne una nuova." });
        }

        var secret = Totp.NewSecret();
        user.TwoFactorPendingSecret = _protector.Protect(secret);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var uri = Totp.OtpAuthUri(Issuer, user.Email, secret);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(6);
        return Ok(new TwoFactorSetupResponse(FormatSecret(Totp.Base32(secret)), uri, "data:image/png;base64," + Convert.ToBase64String(png)));
    }

    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("api/account/2fa/enable")]
    public async Task<ActionResult<RecoveryCodesResponse>> Enable(TwoFactorCodeRequest request, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var secret = _protector.Unprotect(user.TwoFactorPendingSecret);
        if (secret is null)
        {
            return Conflict(new { message = "Avvia prima la configurazione e inquadra il codice QR con l'app." });
        }

        var step = Totp.Verify(secret, request.Code, DateTimeOffset.UtcNow, 0);
        if (step is null)
        {
            return BadRequest(new { message = "Codice non valido: controlla che l'orario del telefono sia automatico e riprova." });
        }

        var codes = Totp.NewRecoveryCodes();
        user.TwoFactorSecret = user.TwoFactorPendingSecret;
        user.TwoFactorPendingSecret = null;
        user.TwoFactorEnabled = true;
        user.TwoFactorLastStep = step.Value;
        user.TwoFactorRecoveryCodes = string.Join(';', codes.Select(Totp.HashRecoveryCode));
        Audit(user, "TwoFactorEnabled", $"Verifica in due passaggi attivata da {user.Email}.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new RecoveryCodesResponse(codes));
    }

    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("api/account/2fa/recovery-codes")]
    public async Task<ActionResult<RecoveryCodesResponse>> NewRecoveryCodes(TwoFactorCodeRequest request, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!user.TwoFactorEnabled || !VerifyAuthenticatorCode(user, request.Code))
        {
            return BadRequest(new { message = "Codice non valido." });
        }

        var codes = Totp.NewRecoveryCodes();
        user.TwoFactorRecoveryCodes = string.Join(';', codes.Select(Totp.HashRecoveryCode));
        Audit(user, "TwoFactorRecoveryCodesRenewed", $"Nuovi codici di recupero per {user.Email}.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new RecoveryCodesResponse(codes));
    }

    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("api/account/2fa/disable")]
    public async Task<IActionResult> Disable(DisableTwoFactorRequest request, CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (await IsRequiredAsync(user.Role, cancellationToken))
        {
            return Conflict(new { message = "Per il tuo ruolo la verifica in due passaggi è obbligatoria: non si può disattivare." });
        }

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty) == PasswordVerificationResult.Failed
            || !VerifyCodeOrRecovery(user, request.Code, _protector))
        {
            return BadRequest(new { message = "Password o codice non corretti." });
        }

        Clear(user);
        Audit(user, "TwoFactorDisabled", $"Verifica in due passaggi disattivata da {user.Email}.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Lost phone and recovery codes: the Admin switches two-factor off for that user, whose sessions
    /// end; at the next login they set it up again (at once, if their role requires it).</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("api/users/{id:guid}/2fa/reset")]
    public async Task<IActionResult> Reset(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        Clear(user);
        foreach (var token in await _dbContext.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(cancellationToken))
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "TwoFactorReset",
            EntityType = "User",
            EntityId = user.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Verifica in due passaggi azzerata per {user.Email} dall'amministratore."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("api/company-profile/security")]
    public async Task<ActionResult<SecuritySettingsResponse>> GetSecurity(CancellationToken cancellationToken = default)
    {
        var stored = await _dbContext.CompanyProfiles.AsNoTracking().Select(p => p.TwoFactorRoles).FirstOrDefaultAsync(cancellationToken);
        return Ok(new SecuritySettingsResponse(TwoFactorRules.ParseRoles(stored).ToList(), AccessChannels.Roles.ToList()));
    }

    /// <summary>Roles that must use two-factor. The Admin setting it for their own role must already have it
    /// active, so saving can't lock them out.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("api/company-profile/security")]
    public async Task<ActionResult<SecuritySettingsResponse>> SaveSecurity(SaveSecuritySettingsRequest request, CancellationToken cancellationToken = default)
    {
        var roles = (request.TwoFactorRoles ?? []).Distinct().ToList();
        var unknown = roles.FirstOrDefault(role => !AccessChannels.Roles.Contains(role));
        if (unknown is not null)
        {
            return BadRequest(new { message = $"Ruolo non riconosciuto: {unknown}." });
        }

        var admin = await CurrentUserAsync(cancellationToken);
        if (admin is not null && roles.Contains(admin.Role) && !admin.TwoFactorEnabled)
        {
            return Conflict(new { message = "Attiva prima la verifica in due passaggi sul tuo account, poi rendila obbligatoria per il tuo ruolo." });
        }

        var profile = await _dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return Conflict(new { message = "Configura prima l'azienda." });
        }

        profile.TwoFactorRoles = string.Join(',', roles);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "TwoFactorPolicySaved",
            EntityType = "CompanyProfile",
            EntityId = profile.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = roles.Count == 0 ? "Verifica in due passaggi facoltativa per tutti." : $"Verifica in due passaggi obbligatoria per: {string.Join(", ", roles)}."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new SecuritySettingsResponse(roles, AccessChannels.Roles.ToList()));
    }

    private async Task<bool> IsRequiredAsync(string role, CancellationToken cancellationToken)
    {
        var stored = await _dbContext.CompanyProfiles.AsNoTracking().Select(p => p.TwoFactorRoles).FirstOrDefaultAsync(cancellationToken);
        return TwoFactorRules.ParseRoles(stored).Contains(role);
    }

    private async Task<User?> CurrentUserAsync(CancellationToken cancellationToken)
    {
        var id = DepartmentFilter.CurrentUserId(User);
        return id is null ? null : await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == id && u.IsActive, cancellationToken);
    }

    private bool VerifyAuthenticatorCode(User user, string? code)
    {
        var secret = _protector.Unprotect(user.TwoFactorSecret);
        var step = secret is null ? null : Totp.Verify(secret, code, DateTimeOffset.UtcNow, user.TwoFactorLastStep);
        if (step is null)
        {
            return false;
        }

        user.TwoFactorLastStep = step.Value;
        return true;
    }

    /// <summary>An authenticator code, or one of the recovery codes (used up once accepted).</summary>
    internal static bool VerifyCodeOrRecovery(User user, string? code, SecretProtector protector)
    {
        var secret = protector.Unprotect(user.TwoFactorSecret);
        var step = secret is null ? null : Totp.Verify(secret, code, DateTimeOffset.UtcNow, user.TwoFactorLastStep);
        if (step is not null)
        {
            user.TwoFactorLastStep = step.Value;
            return true;
        }

        if (string.IsNullOrWhiteSpace(code) || Totp.NormalizeRecoveryCode(code).Length != 8)
        {
            return false;
        }

        var hashes = RecoveryHashes(user);
        if (!hashes.Remove(Totp.HashRecoveryCode(code)))
        {
            return false;
        }

        user.TwoFactorRecoveryCodes = string.Join(';', hashes);
        return true;
    }

    private static List<string> RecoveryHashes(User user) =>
        user.TwoFactorRecoveryCodes.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static void Clear(User user)
    {
        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        user.TwoFactorPendingSecret = null;
        user.TwoFactorRecoveryCodes = string.Empty;
        user.TwoFactorLastStep = 0;
    }

    private void Audit(User user, string action, string details) => _dbContext.AuditLogs.Add(new AuditLog
    {
        Action = action,
        EntityType = "User",
        EntityId = user.Id,
        UserName = user.Email,
        Details = details
    });

    /// <summary>"JBSW Y3DP EHPK 3PXP": easier to type into the app than one long string.</summary>
    private static string FormatSecret(string base32) =>
        string.Join(' ', Enumerable.Range(0, (base32.Length + 3) / 4).Select(i => base32.Substring(i * 4, Math.Min(4, base32.Length - i * 4))));
}

public sealed record TwoFactorStatusResponse(bool Enabled, bool Required, int RecoveryCodesLeft);
public sealed record TwoFactorSetupResponse(string Secret, string OtpAuthUri, string QrCodePng);
public sealed record TwoFactorCodeRequest(string? Code);
public sealed record DisableTwoFactorRequest(string? Password, string? Code);
public sealed record RecoveryCodesResponse(List<string> RecoveryCodes);
public sealed record SecuritySettingsResponse(List<string> TwoFactorRoles, List<string> KnownRoles);
public sealed record SaveSecuritySettingsRequest(List<string>? TwoFactorRoles);
