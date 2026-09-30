using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrmMes.Licensing;

/// <summary>States of a subscription as the vendor console decides them.</summary>
public static class LicenseStatus
{
    /// <summary>Paid: everything the plan includes works.</summary>
    public const string Active = "active";

    /// <summary>Payment late but within the grace period: everything works, with a warning.</summary>
    public const string Grace = "grace";

    /// <summary>Not paid: only a limited overall view (dashboard, lists); no details opened, nothing changed.</summary>
    public const string Suspended = "suspended";

    public static readonly IReadOnlyList<string> All = [Active, Grace, Suspended];
}

/// <summary>What the vendor console grants an installation: plan, paid modules, users, state, and until
/// when the grant holds without a fresh check (an installation offline for a while keeps working).</summary>
public sealed record LicenseGrant(
    Guid InstallationId,
    string Customer,
    string Plan,
    IReadOnlyList<string> Modules,
    int? MaxUsers,
    string Status,
    string? Message,
    DateTime IssuedAt,
    DateTime ValidUntil);

/// <summary>A license is a small signed document: base64url(JSON) "." base64url(ECDSA P-256 signature).
/// The console signs with its private key; each installation verifies with the console's public key, so a
/// license can't be edited (a module added, a suspension removed) without breaking the signature.</summary>
public static class LicenseToken
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Sign(LicenseGrant grant, ECDsa privateKey)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(grant, Json);
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256);
        return Base64Url(payload) + "." + Base64Url(signature);
    }

    /// <summary>The grant, or null when the token is malformed or not signed by this key. Expiry is the
    /// caller's decision (see <see cref="Effective"/>): an expired grant still says what was granted.</summary>
    public static LicenseGrant? Verify(string? token, ECDsa publicKey)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        try
        {
            var payload = FromBase64Url(parts[0]);
            var signature = FromBase64Url(parts[1]);
            return publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256)
                ? JsonSerializer.Deserialize<LicenseGrant>(payload, Json)
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>The state to apply now: a grant past its validity (no successful check with the console
    /// for too long) counts as suspended, with a message that says why.</summary>
    public static (string Status, string? Message) Effective(LicenseGrant grant, DateTime utcNow) =>
        utcNow <= grant.ValidUntil
            ? (grant.Status, grant.Message)
            : (LicenseStatus.Suspended, "Non è stato possibile verificare l'abbonamento da troppo tempo: controlla la connessione a internet del server o contatta l'assistenza.");

    public static string ExportPublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    public static ECDsa ImportPublicKey(string base64)
    {
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64), out _);
        return key;
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }
}

/// <summary>What an installation tells the console every hour: enough to see it is alive, up to date and
/// what it uses. No company data (customers, orders, prices) ever leaves the installation.</summary>
public sealed record Heartbeat(
    string Version,
    string Hosting,
    string? CompanyName,
    string? VatNumber,
    IReadOnlyList<string> EnabledModules,
    int ActiveUsers,
    long? DatabaseSizeBytes,
    bool DatabaseOk,
    DateTime SentAt);

/// <summary>The console's answer: the signed license and its public key (pinned by the installation at the
/// first contact, then required to match).</summary>
public sealed record HeartbeatReply(string License, string PublicKey);
