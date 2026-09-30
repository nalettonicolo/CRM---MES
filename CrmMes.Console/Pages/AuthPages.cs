using CrmMes.Console.Data;
using CrmMes.Console.Services;
using CrmMes.Core.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace CrmMes.Console.Pages;

/// <summary>First start only (no console user yet): the vendor's own account, then the authenticator setup.</summary>
[EnableRateLimiting("login")]
public class SetupModel(ConsoleDbContext db, IPasswordHasher<ConsoleUser> hasher) : PageModel
{
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await db.Users.AnyAsync() ? RedirectToPage("/Login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (await db.Users.AnyAsync())
        {
            return RedirectToPage("/Login");
        }

        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Email) || Password.Length < 12)
        {
            Error = "Nome, email e una password di almeno 12 caratteri sono obbligatori.";
            return Page();
        }

        var user = new ConsoleUser { Name = Name.Trim(), Email = Email.Trim().ToLowerInvariant() };
        user.PasswordHash = hasher.HashPassword(user, Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await ConsoleAuth.SignInAsync(HttpContext, user, ConsoleAuth.StageEnroll);
        return RedirectToPage("/Enroll");
    }
}

[EnableRateLimiting("login")]
public class LoginModel(ConsoleDbContext db, IPasswordHasher<ConsoleUser> hasher) : PageModel
{
    private static readonly string DummyHash = new PasswordHasher<ConsoleUser>().HashPassword(new ConsoleUser(), "equalizer-not-a-password");

    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await db.Users.AnyAsync())
        {
            return RedirectToPage("/Setup");
        }

        await HttpContext.SignOutAsync(ConsoleAuth.Scheme);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var email = Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email && u.IsActive);
        if (user is null)
        {
            hasher.VerifyHashedPassword(new ConsoleUser(), DummyHash, Password);
            Error = "Credenziali non valide.";
            return Page();
        }

        if (user.LockedUntil is { } locked && locked > DateTime.UtcNow)
        {
            Error = "Troppi tentativi errati: riprova tra qualche minuto.";
            return Page();
        }

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, Password) == PasswordVerificationResult.Failed)
        {
            user.FailedLogins++;
            if (user.FailedLogins >= 5)
            {
                user.FailedLogins = 0;
                user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
            }

            await db.SaveChangesAsync();
            Error = "Credenziali non valide.";
            return Page();
        }

        user.FailedLogins = 0;
        user.LockedUntil = null;
        await db.SaveChangesAsync();
        // Two-factor is compulsory: the code if set up, otherwise the setup itself.
        var stage = user.TotpSecret is null ? ConsoleAuth.StageEnroll : ConsoleAuth.StageCode;
        await ConsoleAuth.SignInAsync(HttpContext, user, stage);
        return RedirectToPage(stage == ConsoleAuth.StageEnroll ? "/Enroll" : "/LoginCode");
    }
}

[EnableRateLimiting("login")]
public class LoginCodeModel(ConsoleDbContext db, SecretProtector protector) : PageModel
{
    [BindProperty] public string Code { get; set; } = string.Empty;
    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var id = ConsoleAuth.UserId(User);
        var user = id is null ? null : await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.IsActive);
        var secret = protector.Unprotect(user?.TotpSecret);
        var step = secret is null ? null : Totp.Verify(secret, Code, DateTimeOffset.UtcNow, user!.TotpLastStep);
        if (user is null || step is null)
        {
            if (user is not null)
            {
                user.FailedLogins++;
                if (user.FailedLogins >= 5)
                {
                    user.FailedLogins = 0;
                    user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
                    await db.SaveChangesAsync();
                    await HttpContext.SignOutAsync(ConsoleAuth.Scheme);
                    return RedirectToPage("/Login");
                }

                await db.SaveChangesAsync();
            }

            Error = "Codice non valido.";
            return Page();
        }

        user.TotpLastStep = step.Value;
        user.FailedLogins = 0;
        await db.SaveChangesAsync();
        await ConsoleAuth.SignInAsync(HttpContext, user, ConsoleAuth.StageFull);
        return RedirectToPage("/Index");
    }
}

/// <summary>Authenticator setup, compulsory before using the console.</summary>
public class EnrollModel(ConsoleDbContext db, SecretProtector protector) : PageModel
{
    [BindProperty] public string Code { get; set; } = string.Empty;
    public string QrCode { get; private set; } = string.Empty;
    public string SecretText { get; private set; } = string.Empty;
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await PrepareAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await CurrentAsync();
        if (user is null)
        {
            return RedirectToPage("/Login");
        }

        var pending = protector.Unprotect(user.TotpPendingSecret);
        var step = pending is null ? null : Totp.Verify(pending, Code, DateTimeOffset.UtcNow, 0);
        if (step is null)
        {
            Error = "Codice non valido: controlla che l'orario del telefono sia automatico e riprova.";
            return await PrepareAsync();
        }

        user.TotpSecret = user.TotpPendingSecret;
        user.TotpPendingSecret = null;
        user.TotpLastStep = step.Value;
        await db.SaveChangesAsync();
        await ConsoleAuth.SignInAsync(HttpContext, user, ConsoleAuth.StageFull);
        return RedirectToPage("/Index");
    }

    private async Task<IActionResult> PrepareAsync()
    {
        var user = await CurrentAsync();
        if (user is null)
        {
            return RedirectToPage("/Login");
        }

        var secret = protector.Unprotect(user.TotpPendingSecret);
        if (secret is null)
        {
            secret = Totp.NewSecret();
            user.TotpPendingSecret = protector.Protect(secret);
            await db.SaveChangesAsync();
        }

        var uri = Totp.OtpAuthUri("Console Nicolò MES", user.Email, secret);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        QrCode = "data:image/png;base64," + Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(6));
        SecretText = Totp.Base32(secret);
        return Page();
    }

    private async Task<ConsoleUser?> CurrentAsync()
    {
        var id = ConsoleAuth.UserId(User);
        return id is null ? null : await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.IsActive);
    }
}

public class LogoutModel : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(ConsoleAuth.Scheme);
        return RedirectToPage("/Login");
    }
}
