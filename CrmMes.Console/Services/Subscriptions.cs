using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrmMes.Console.Data;
using CrmMes.Core.Security;
using CrmMes.Licensing;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Console.Services;

/// <summary>Whether a customer is in good standing, from what it has paid. The same rule for Stripe and bank
/// transfer: paid through PaidUntil; late but within the grace days (everything works, with a warning);
/// beyond them, suspended (limited overall view only). A manual decision overrides the rule.</summary>
public static class SubscriptionRules
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public static (string Status, string? Message) Evaluate(Customer customer, DateTime utcNow)
    {
        if (customer.ForcedStatus == LicenseStatus.Suspended)
        {
            return (LicenseStatus.Suspended, "Abbonamento sospeso dall'assistenza: contattala per riattivarlo.");
        }

        if (customer.ForcedStatus == LicenseStatus.Active)
        {
            return (LicenseStatus.Active, null);
        }

        var today = utcNow.Date;
        if (customer.PaidUntil is null)
        {
            var firstDeadline = customer.CreatedAt.Date.AddDays(customer.GraceDays);
            return today <= firstDeadline
                ? (LicenseStatus.Grace, $"In attesa del primo pagamento: il servizio resta attivo fino al {firstDeadline.ToString("d", Italian)}.")
                : (LicenseStatus.Suspended, "Abbonamento sospeso: primo pagamento non ricevuto. Contatta l'assistenza.");
        }

        var paidUntil = customer.PaidUntil.Value.Date;
        if (today <= paidUntil)
        {
            return (LicenseStatus.Active, null);
        }

        var suspendOn = paidUntil.AddDays(customer.GraceDays);
        return today <= suspendOn
            ? (LicenseStatus.Grace, $"Pagamento dell'abbonamento in ritardo dal {paidUntil.AddDays(1).ToString("d", Italian)}: il servizio verrà sospeso il {suspendOn.AddDays(1).ToString("d", Italian)}.")
            : (LicenseStatus.Suspended, $"Abbonamento sospeso per mancato pagamento dal {paidUntil.AddDays(1).ToString("d", Italian)}. Contatta l'assistenza per riattivarlo.");
    }

    public static string StatusName(string status) => status switch
    {
        LicenseStatus.Active => "In regola",
        LicenseStatus.Grace => "In ritardo",
        LicenseStatus.Suspended => "Sospeso",
        _ => status,
    };

    /// <summary>An installation that hasn't reported for more than two hours (it reports every hour).</summary>
    public static bool IsOnline(Installation installation, DateTime utcNow) =>
        installation.LastHeartbeatAt is { } last && utcNow - last < TimeSpan.FromHours(2);
}

/// <summary>The monthly fee from the price list: plan + each paid module + extra users.</summary>
public static class Pricing
{
    public static IReadOnlyList<string> ParseModules(string? stored) =>
        (stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(key => ModuleCatalog.All.Any(m => m.Key == key)).Distinct().ToList();

    public static decimal MonthlyTotal(Customer customer, IReadOnlyCollection<PriceItem> prices)
    {
        var plan = prices.FirstOrDefault(p => p.Kind == "plan" && p.Key == customer.PlanKey);
        var modules = ParseModules(customer.Modules).Sum(key => prices.FirstOrDefault(p => p.Kind == "module" && p.Key == key)?.MonthlyPrice ?? 0);
        var user = prices.FirstOrDefault(p => p.Kind == "user")?.MonthlyPrice ?? 0;
        return (plan?.MonthlyPrice ?? 0) + modules + customer.ExtraUsers * user;
    }

    public static int? MaxUsers(Customer customer, IReadOnlyCollection<PriceItem> prices)
    {
        var included = prices.FirstOrDefault(p => p.Kind == "plan" && p.Key == customer.PlanKey)?.IncludedUsers;
        return included is null ? null : included + customer.ExtraUsers;
    }

    /// <summary>A starting price list, changed from the Prices page: nothing here is a commercial decision.</summary>
    public static async Task SeedAsync(ConsoleDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Prices.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Prices.AddRange(
            new PriceItem { Key = "base", Kind = "plan", Name = "Base", MonthlyPrice = 49, IncludedUsers = 3, SortOrder = 1 },
            new PriceItem { Key = "standard", Kind = "plan", Name = "Standard", MonthlyPrice = 99, IncludedUsers = 10, SortOrder = 2 },
            new PriceItem { Key = "pro", Kind = "plan", Name = "Pro", MonthlyPrice = 199, IncludedUsers = 30, SortOrder = 3 },
            new PriceItem { Key = "user", Kind = "user", Name = "Utente aggiuntivo", MonthlyPrice = 8, SortOrder = 10 });
        var order = 20;
        foreach (var module in ModuleCatalog.All)
        {
            db.Prices.Add(new PriceItem { Key = module.Key, Kind = "module", Name = module.Name, MonthlyPrice = 15, SortOrder = order++ });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The console's signing key: created at the first start, stored in the database encrypted with
/// CONSOLE_SECRET. Its public half goes to every installation, which pins it at the first contact.</summary>
public sealed class ConsoleKeys
{
    private const string SettingKey = "license-signing-key";
    private readonly ECDsa _key;

    private ConsoleKeys(ECDsa key)
    {
        _key = key;
        PublicKey = LicenseToken.ExportPublicKey(key);
    }

    public string PublicKey { get; }

    public string Sign(LicenseGrant grant) => LicenseToken.Sign(grant, _key);

    public static async Task<ConsoleKeys> LoadOrCreateAsync(ConsoleDbContext db, SecretProtector protector, CancellationToken cancellationToken = default)
    {
        var stored = await db.Settings.SingleOrDefaultAsync(s => s.Key == SettingKey, cancellationToken);
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (stored is not null)
        {
            var pkcs8 = protector.Unprotect(stored.Value)
                ?? throw new InvalidOperationException("Chiave di firma illeggibile: CONSOLE_SECRET è cambiato? Senza la stessa chiave le installazioni rifiutano le licenze.");
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return new ConsoleKeys(key);
        }

        db.Settings.Add(new ConsoleSetting { Key = SettingKey, Value = protector.Protect(key.ExportPkcs8PrivateKey()) });
        await db.SaveChangesAsync(cancellationToken);
        return new ConsoleKeys(key);
    }
}

/// <summary>License keys given to installations: "nmes_" + 32 random characters, shown once, kept as a hash.</summary>
public static class LicenseKeys
{
    public static string New()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        return "nmes_" + new string(RandomNumberGenerator.GetItems<char>(alphabet, 32));
    }

    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim())));

    public static string Prefix(string key) => key.Length <= 10 ? key : key[..10];
}
