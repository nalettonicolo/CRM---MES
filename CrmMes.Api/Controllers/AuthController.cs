using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Security;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CrmMes.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(CrmMes.Api.Services.RateLimits.Auth)]
public class AuthController : ControllerBase
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    private static readonly HashSet<string> AllowedExternalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin", "Management", "Warehouse", "Purchasing", "Sales", "Operator"
    };

    /// <summary>Wrong passwords in a row before the account is locked, and for how long.</summary>
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>A throwaway hash verified when the email doesn't exist, so a login for an unknown email
    /// costs the same time as a wrong password: otherwise response timing reveals which emails exist.</summary>
    private static readonly string DummyPasswordHash =
        new PasswordHasher<User>().HashPassword(new User(), "timing-equalizer-not-a-real-password");

    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly TwoFactorChallenges _challenges;
    private readonly SecretProtector _protector;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        IPasswordHasher<User> passwordHasher,
        TwoFactorChallenges challenges,
        SecretProtector protector,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
        _challenges = challenges;
        _protector = protector;
        _environment = environment;
    }

    private bool ExternalAuthAllowed =>
        _environment.IsDevelopment() || _configuration.GetValue<bool>("Auth:External:Enabled");

    /// <summary>OIDC/SSO exchange: clients send verified IdP claims after their login flow. In production enable
    /// only with a trusted token mapper; this endpoint is the server-side plumbing.</summary>
    [HttpPost("external")]
    public async Task<ActionResult<AuthResponse>> ExternalLogin(
        ExternalLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ExternalAuthAllowed)
        {
            return NotFound();
        }

        var provider = request.Provider?.Trim();
        var subject = request.Subject?.Trim();
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(subject))
        {
            return BadRequest(new { message = "Provider e Subject sono obbligatori." });
        }

        var email = request.Email?.Trim().ToLowerInvariant();
        var name = request.Name?.Trim();
        var channel = AccessChannels.NormalizeChannel(request.Channel) ?? AccessChannels.Web;

        var user = await _dbContext.Users.SingleOrDefaultAsync(
            u => u.ExternalProvider == provider && u.ExternalSubject == subject && u.IsActive, cancellationToken);

        if (user is null && !string.IsNullOrWhiteSpace(email))
        {
            user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);
            if (user is not null)
            {
                user.ExternalProvider = provider;
                user.ExternalSubject = subject;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    user.Name = name;
                }
            }
        }

        if (user is null)
        {
            if (!_configuration.GetValue<bool>("Auth:External:AutoProvision"))
            {
                return Unauthorized(new { message = "Utente non registrato: chiedi a un Admin di abilitare l'accesso SSO." });
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { message = "Email e nome sono obbligatori per creare un nuovo utente SSO." });
            }

            if (await _dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken))
            {
                return Conflict(new { message = "Email già registrata con un altro account." });
            }

            var role = _configuration["Auth:External:DefaultRole"]?.Trim();
            if (string.IsNullOrWhiteSpace(role))
            {
                role = "Operator";
            }

            if (!AllowedExternalRoles.Contains(role))
            {
                return BadRequest(new { message = "Ruolo predefinito SSO non valido." });
            }

            user = new User
            {
                Name = name,
                Email = email,
                Role = role,
                PasswordHash = string.Empty,
                ExternalProvider = provider,
                ExternalSubject = subject,
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var channelRefusal = await ChannelRefusalAsync(user.Role, channel, cancellationToken);
        if (channelRefusal is not null)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = channelRefusal });
        }

        if (user.TwoFactorEnabled)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new AuthResponse(string.Empty, string.Empty, DateTime.UtcNow.Add(TwoFactorChallenges.Lifetime),
                user.Id, user.Name, user.Email, user.Role, TwoFactorChallenge: _challenges.Issue(user.Id, channel)));
        }

        return Ok(await CreateResponseAsync(user, cancellationToken, channel: channel));
    }

    [HttpGet("external/providers")]
    public ActionResult<List<ExternalProviderResponse>> ExternalProviders()
    {
        if (!ExternalAuthAllowed)
        {
            return Ok(new List<ExternalProviderResponse>());
        }

        var list = new List<ExternalProviderResponse>();
        var section = _configuration.GetSection("Auth:External:Providers");
        foreach (var child in section.GetChildren())
        {
            if (child.Exists() && !string.IsNullOrWhiteSpace(child["Key"]))
            {
                list.Add(new ExternalProviderResponse(
                    child["Key"]!.Trim(),
                    child["Name"]?.Trim() ?? child["Key"]!.Trim(),
                    child.GetValue<bool>("Enabled")));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(child.Key) && child.Key != "0")
            {
                list.Add(new ExternalProviderResponse(
                    child.Key,
                    child["Name"]?.Trim() ?? child.Key,
                    child.GetValue("Enabled", true)));
            }
        }

        return Ok(list);
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        var name = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return BadRequest(new { message = "Nome, email e password di almeno 8 caratteri sono obbligatori." });
        }

        // Bootstrap-only: questo endpoint resta anonimo esclusivamente per creare il primo
        // Admin di un ambiente nuovo senza richiedere accesso diretto al database. Una volta che
        // esiste almeno un utente, l'auto-registrazione pubblica è chiusa: solo un Admin autenticato
        // può crearne altri, da POST /api/users (vedi UsersController.CreateUser, AdminOnly).
        if (await _dbContext.Users.AnyAsync(cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "La registrazione pubblica è disabilitata: chiedi a un Admin di crearti un account." });
        }

        if (await _dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return Conflict(new { message = "Email già registrata." });
        }

        var user = new User
        {
            Name = name,
            Email = email,
            Role = "Admin"
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await CreateResponseAsync(user, cancellationToken));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Email == email && item.IsActive, cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            _passwordHasher.VerifyHashedPassword(new User(), DummyPasswordHash, request.Password ?? string.Empty);
            return Unauthorized(new { message = "Credenziali non valide." });
        }

        var now = DateTime.UtcNow;
        if (user.LockoutEndsAt is { } lockedUntil && lockedUntil > now)
        {
            // Same answer whether or not the password is right: a locked account gives no signal.
            var minutes = (int)Math.Ceiling((lockedUntil - now).TotalMinutes);
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = $"Troppi tentativi errati: account bloccato per {minutes} minuti." });
        }

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.FailedLoginCount = 0;
                user.LockoutEndsAt = now.Add(LockoutDuration);
                _dbContext.AuditLogs.Add(new AuditLog
                {
                    Action = "UserLockedOut",
                    EntityType = "User",
                    EntityId = user.Id,
                    UserName = user.Email,
                    Details = $"Account {user.Email} bloccato per {LockoutDuration.TotalMinutes:0} minuti dopo {MaxFailedLogins} password errate."
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Unauthorized(new { message = "Credenziali non valide." });
        }

        if (user.FailedLoginCount != 0 || user.LockoutEndsAt is not null)
        {
            user.FailedLoginCount = 0;
            user.LockoutEndsAt = null;
        }

        // Checked only after the password: a refusal must not tell a stranger that the account exists.
        var channel = AccessChannels.NormalizeChannel(request.Channel);
        if (channel is null)
        {
            return BadRequest(new { message = "Canale di accesso non riconosciuto." });
        }

        var channelRefusal = await ChannelRefusalAsync(user.Role, channel, cancellationToken);
        if (channelRefusal is not null)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = channelRefusal });
        }

        if (user.TwoFactorEnabled)
        {
            // Password right, now the code: no tokens yet, only a 5-minute challenge.
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new AuthResponse(string.Empty, string.Empty, DateTime.UtcNow.Add(TwoFactorChallenges.Lifetime),
                user.Id, user.Name, user.Email, user.Role, TwoFactorChallenge: _challenges.Issue(user.Id, channel)));
        }

        return Ok(await CreateResponseAsync(user, cancellationToken, channel: channel));
    }

    /// <summary>Second step of a login with two-factor: the challenge from the first step and a code from the
    /// app, or one of the recovery codes. Wrong codes count as wrong passwords (same lockout).</summary>
    [HttpPost("login/2fa")]
    public async Task<ActionResult<AuthResponse>> LoginTwoFactor(TwoFactorLoginRequest request, CancellationToken cancellationToken = default)
    {
        var challenge = _challenges.Read(request.Challenge);
        if (challenge is null)
        {
            return Unauthorized(new { message = "Accesso scaduto: inserisci di nuovo email e password." });
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == challenge.Value.UserId && u.IsActive, cancellationToken);
        if (user is null || !user.TwoFactorEnabled)
        {
            return Unauthorized(new { message = "Accesso scaduto: inserisci di nuovo email e password." });
        }

        var now = DateTime.UtcNow;
        if (user.LockoutEndsAt is { } lockedUntil && lockedUntil > now)
        {
            var minutes = (int)Math.Ceiling((lockedUntil - now).TotalMinutes);
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = $"Troppi tentativi errati: account bloccato per {minutes} minuti." });
        }

        if (!AccountSecurityController.VerifyCodeOrRecovery(user, request.Code, _protector))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.FailedLoginCount = 0;
                user.LockoutEndsAt = now.Add(LockoutDuration);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Unauthorized(new { message = "Codice non valido." });
        }

        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        return Ok(await CreateResponseAsync(user, cancellationToken, channel: challenge.Value.Channel));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(
        RefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Unauthorized(new { message = "Refresh token non valido." });
        }

        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .SingleOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

        if (existing is null || !existing.IsActive || !existing.User.IsActive)
        {
            return Unauthorized(new { message = "Refresh token non valido o scaduto." });
        }

        existing.RevokedAt = DateTime.UtcNow;
        var sessionChannel = AccessChannels.NormalizeChannel(existing.Channel) ?? AccessChannels.Desktop;
        var channelRefusal = await ChannelRefusalAsync(existing.User.Role, sessionChannel, cancellationToken);
        if (channelRefusal is not null)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = channelRefusal });
        }

        var response = await CreateResponseAsync(existing.User, cancellationToken, replaces: existing, channel: sessionChannel);
        return Ok(response);
    }

    /// <summary>Changes the caller's own password. The current one is required (a stolen, still-valid
    /// access token alone can't take over the account), the new one must differ and be at least 8
    /// characters. Every refresh token of the user is revoked: sessions on other PCs end at their next
    /// token renewal, at most 30 minutes later.</summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var userIdText = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdText, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            return BadRequest(new { message = "La password attuale non è corretta." });
        }

        var validation = ValidateNewPassword(request.NewPassword, request.CurrentPassword);
        if (validation is not null)
        {
            return BadRequest(new { message = validation });
        }

        await SetPasswordAsync(_dbContext, _passwordHasher, user, request.NewPassword!, cancellationToken);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "PasswordChanged",
            EntityType = "User",
            EntityId = user.Id,
            UserName = user.Email,
            Details = $"Password cambiata da {user.Email}; sessioni aperte revocate."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Minimum rules for a new password; null when acceptable.</summary>
    internal static string? ValidateNewPassword(string? newPassword, string? currentPassword = null)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            return "La nuova password deve avere almeno 8 caratteri.";
        }

        if (currentPassword is not null && newPassword == currentPassword)
        {
            return "La nuova password deve essere diversa da quella attuale.";
        }

        return null;
    }

    /// <summary>Sets a new password hash, clears any lockout and revokes every refresh token of the user
    /// (caller saves). Shared by self-service change and Admin reset.</summary>
    internal static async Task SetPasswordAsync(
        ApplicationDbContext dbContext, IPasswordHasher<User> passwordHasher, User user, string newPassword, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;

        var now = DateTime.UtcNow;
        var activeTokens = await dbContext.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        RefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return NoContent();
        }

        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.RefreshTokens.SingleOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);
        if (existing is not null && existing.RevokedAt is null)
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    /// <summary>Why this role may not log in from this channel, or null when it may.</summary>
    private async Task<string?> ChannelRefusalAsync(string role, string channel, CancellationToken cancellationToken)
    {
        var stored = await _dbContext.CompanyProfiles.AsNoTracking().Select(p => p.AccessChannels).FirstOrDefaultAsync(cancellationToken);
        var settings = AccessChannels.Parse(stored);
        if (AccessChannels.CanLogIn(settings, role, channel))
        {
            return null;
        }

        return settings.Channels.Contains(channel)
            ? $"Il tuo ruolo non può accedere dalla {AccessChannels.ChannelName(channel)}. Chiedi all'amministratore."
            : $"L'azienda non usa la {AccessChannels.ChannelName(channel)}. Chiedi all'amministratore.";
    }

    private async Task<AuthResponse> CreateResponseAsync(
        User user, CancellationToken cancellationToken, RefreshToken? replaces = null, string channel = AccessChannels.Desktop)
    {
        var key = _configuration["Jwt:Key"]
            ?? Environment.GetEnvironmentVariable("CRM_MES_JWT_KEY")
            ?? "development-only-key-change-before-deploy-32chars";
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("channel", channel)
        };

        // Compulsory for this role and not set up yet: the session is limited to setting it up.
        var requiredRoles = TwoFactorRules.ParseRoles(await _dbContext.CompanyProfiles.AsNoTracking()
            .Select(p => p.TwoFactorRoles).FirstOrDefaultAsync(cancellationToken));
        var setupRequired = !user.TwoFactorEnabled && requiredRoles.Contains(user.Role);
        if (setupRequired)
        {
            claims.Add(new Claim(TwoFactorRules.SetupClaim, "required"));
        }
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.Add(AccessTokenLifetime);
        var token = new JwtSecurityToken(claims: claims, expires: expiresAt, signingCredentials: credentials);

        var rawRefreshToken = GenerateRefreshToken();
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawRefreshToken),
            ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime),
            Channel = channel
        };
        _dbContext.RefreshTokens.Add(refreshToken);

        if (replaces is not null)
        {
            replaces.ReplacedByTokenId = refreshToken.Id;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            rawRefreshToken,
            expiresAt,
            user.Id,
            user.Name,
            user.Email,
            user.Role,
            TwoFactorSetupRequired: setupRequired);
    }

    private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed record RegisterRequest(string? Name, string? Email, string? Password);
/// <summary>Channel: "desktop" (also when missing, older clients), "web" or "mobile".</summary>
public sealed record LoginRequest(string? Email, string? Password, string? Channel = null);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record RefreshRequest(string? RefreshToken);
/// <summary>With two-factor active the first login step returns no tokens but a TwoFactorChallenge to send
/// to login/2fa with the code. TwoFactorSetupRequired: the role requires two-factor and the account hasn't
/// set it up; the session only allows setting it up.</summary>
public sealed record AuthResponse(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Name,
    string Email,
    string Role,
    string? TwoFactorChallenge = null,
    bool TwoFactorSetupRequired = false);

public sealed record TwoFactorLoginRequest(string? Challenge, string? Code);

public sealed record ExternalLoginRequest(string? Provider, string? Subject, string? Email, string? Name, string? Channel = null);

public sealed record ExternalProviderResponse(string Key, string Name, bool Enabled);
