using System.Security.Claims;
using CrmMes.Console.Data;
using CrmMes.Core.Security;
using CrmMes.Licensing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Console.Services;

/// <summary>Loads (or creates at the first start) the signing key once, on first use.</summary>
public sealed class ConsoleKeyStore
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SecretProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConsoleKeys? _keys;

    public ConsoleKeyStore(IServiceScopeFactory scopes, SecretProtector protector)
    {
        _scopes = scopes;
        _protector = protector;
    }

    public async Task<ConsoleKeys> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_keys is not null)
        {
            return _keys;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_keys is null)
            {
                using var scope = _scopes.CreateScope();
                _keys = await ConsoleKeys.LoadOrCreateAsync(scope.ServiceProvider.GetRequiredService<ConsoleDbContext>(), _protector, cancellationToken);
            }

            return _keys;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>The contact point of the installations: telemetry in, signed license out.</summary>
public static class HeartbeatEndpoint
{
    public const string KeyHeader = "X-License-Key";
    public static readonly TimeSpan LicenseValidity = TimeSpan.FromDays(30);

    public static async Task<IResult> HandleAsync(HttpContext context, Heartbeat heartbeat, ConsoleDbContext db, ConsoleKeyStore keys, CancellationToken cancellationToken)
    {
        var key = context.Request.Headers[KeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return Results.Unauthorized();
        }

        var hash = LicenseKeys.Hash(key);
        var installation = await db.Installations.Include(i => i.Customer).SingleOrDefaultAsync(i => i.KeyHash == hash, cancellationToken);
        if (installation is null || installation.Revoked)
        {
            return Results.Unauthorized();
        }

        var now = DateTime.UtcNow;
        installation.LastHeartbeatAt = now;
        installation.Version = Trim(heartbeat.Version, 40);
        installation.Hosting = Trim(heartbeat.Hosting, 20);
        installation.CompanyName = Trim(heartbeat.CompanyName, 250);
        installation.VatNumber = Trim(heartbeat.VatNumber, 30);
        installation.ActiveUsers = heartbeat.ActiveUsers;
        installation.DatabaseSizeBytes = heartbeat.DatabaseSizeBytes;
        installation.EnabledModules = Trim(string.Join(',', heartbeat.EnabledModules ?? []), 1000);
        installation.DatabaseOk = heartbeat.DatabaseOk;
        installation.LastIpAddress = context.Connection.RemoteIpAddress?.ToString();

        var customer = installation.Customer;
        var prices = await db.Prices.AsNoTracking().ToListAsync(cancellationToken);
        var (status, message) = SubscriptionRules.Evaluate(customer, now);
        var plan = prices.FirstOrDefault(p => p.Kind == "plan" && p.Key == customer.PlanKey)?.Name ?? customer.PlanKey;
        var grant = new LicenseGrant(installation.Id, customer.Name, plan, Pricing.ParseModules(customer.Modules),
            Pricing.MaxUsers(customer, prices), status, message, now, now.Add(LicenseValidity));
        installation.LastStatusSent = status;
        await db.SaveChangesAsync(cancellationToken);

        var signer = await keys.GetAsync(cancellationToken);
        return Results.Ok(new HeartbeatReply(signer.Sign(grant), signer.PublicKey));
    }

    private static string? Trim(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, max)];
}

/// <summary>Requests for help from the installations (same license key as the heartbeat).</summary>
public static class SupportEndpoint
{
    public static async Task<IResult> CreateAsync(HttpContext context, SupportRequest request, ConsoleDbContext db, CancellationToken cancellationToken)
    {
        var installation = await FindAsync(context, db, cancellationToken);
        if (installation is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { message = "Oggetto e descrizione sono obbligatori." });
        }

        for (var attempt = 0; ; attempt++)
        {
            var ticket = new SupportTicket
            {
                Number = (await db.Tickets.MaxAsync(t => (int?)t.Number, cancellationToken) ?? 0) + 1,
                InstallationId = installation.Id,
                CustomerId = installation.CustomerId,
                Subject = Cut(request.Subject, 200)!,
                Message = Cut(request.Message, 4000)!,
                RequestedBy = Cut(request.RequestedBy, 250) ?? "?",
                Contact = Cut(request.Contact, 200),
                RemoteSessionId = Cut(request.RemoteSessionId, 50),
                DiagnosticsJson = request.Diagnostics is { } diagnostics ? Cut(diagnostics.GetRawText(), 20_000) : null,
            };
            db.Tickets.Add(ticket);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToInfo(ticket));
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                db.Entry(ticket).State = EntityState.Detached; // two requests took the same number: next one
            }
        }
    }

    public static async Task<IResult> ListAsync(HttpContext context, ConsoleDbContext db, CancellationToken cancellationToken)
    {
        var installation = await FindAsync(context, db, cancellationToken);
        if (installation is null)
        {
            return Results.Unauthorized();
        }

        var tickets = await db.Tickets.AsNoTracking().Where(t => t.InstallationId == installation.Id)
            .OrderByDescending(t => t.CreatedAt).Take(50).ToListAsync(cancellationToken);
        return Results.Ok(tickets.Select(ToInfo).ToList());
    }

    public static SupportTicketInfo ToInfo(SupportTicket t) =>
        new(t.Id, t.Number, t.Subject, t.Status, t.CreatedAt, t.RequestedBy, t.Reply, t.RepliedAt);

    private static async Task<Installation?> FindAsync(HttpContext context, ConsoleDbContext db, CancellationToken cancellationToken)
    {
        var key = context.Request.Headers[HeartbeatEndpoint.KeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var hash = LicenseKeys.Hash(key);
        return await db.Installations.SingleOrDefaultAsync(i => i.KeyHash == hash && !i.Revoked, cancellationToken);
    }

    private static string? Cut(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, max)];
}

/// <summary>Console sign-in in two steps: password, then the authenticator code. Until the code (or, for a
/// new user, the authenticator setup) the session can only reach those pages.</summary>
public static class ConsoleAuth
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string StageClaim = "stage";
    public const string StageCode = "code";
    public const string StageEnroll = "enroll";
    public const string StageFull = "full";
    public const string FullPolicy = "ConsoleFull";
    public const string EnrollPolicy = "ConsoleEnroll";
    public const string CodePolicy = "ConsoleCode";

    public static async Task SignInAsync(HttpContext context, ConsoleUser user, string stage)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(StageClaim, stage),
        ], Scheme);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = stage == StageFull ? DateTimeOffset.UtcNow.AddHours(8) : DateTimeOffset.UtcNow.AddMinutes(10),
        });
    }

    public static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static async Task AuditAsync(ConsoleDbContext db, ClaimsPrincipal user, string action, string details)
    {
        db.Audit.Add(new ConsoleAudit { User = user.FindFirstValue(ClaimTypes.Email) ?? "?", Action = action, Details = details });
        await db.SaveChangesAsync();
    }
}
