using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using CrmMes.Licensing;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Services;

/// <summary>The subscription as this installation knows it. Licensing is on only when the installation has
/// been given a console address and a license key (License:ConsoleUrl, License:Key, in server.json or in the
/// environment); otherwise everything is allowed, as before (own use, development, tests).</summary>
public sealed class LicenseState
{
    public sealed record Snapshot(
        bool Enabled, string Status, string? Message, string? Plan, IReadOnlyList<string>? Modules, int? MaxUsers,
        DateTime? ValidUntil, DateTime? CheckedAt, string? Customer)
    {
        public bool IsSuspended => Enabled && Status == LicenseStatus.Suspended;

        /// <summary>Modules the company may switch on: all when licensing is off or no license arrived yet.</summary>
        public IEnumerable<string> Allow(IEnumerable<string> modules) =>
            Modules is null ? modules : modules.Where(m => Modules.Contains(m, StringComparer.OrdinalIgnoreCase));
    }

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopes;
    private readonly object _gate = new();
    private Snapshot? _cached;
    private DateTime _cachedAt;

    public LicenseState(IConfiguration configuration, IServiceScopeFactory scopes)
    {
        _scopes = scopes;
        ConsoleUrl = configuration["License:ConsoleUrl"]?.Trim().TrimEnd('/');
        Key = configuration["License:Key"]?.Trim();
    }

    public string? ConsoleUrl { get; }
    public string? Key { get; }
    public bool Enabled => !string.IsNullOrEmpty(ConsoleUrl) && !string.IsNullOrEmpty(Key);

    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }

    public async Task<Snapshot> CurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return new Snapshot(false, LicenseStatus.Active, null, null, null, null, null, null, null);
        }

        lock (_gate)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAt < CacheFor)
            {
                return _cached;
            }
        }

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.CompanyProfiles.AsNoTracking()
            .Select(p => new { p.LicenseToken, p.LicensePublicKey, p.LicenseCheckedAt })
            .FirstOrDefaultAsync(cancellationToken);
        var snapshot = Evaluate(stored?.LicenseToken, stored?.LicensePublicKey, stored?.LicenseCheckedAt, DateTime.UtcNow);
        lock (_gate)
        {
            _cached = snapshot;
            _cachedAt = DateTime.UtcNow;
        }

        return snapshot;
    }

    /// <summary>No license yet (just installed, console not reached): everything works with a warning, so a
    /// new installation is never blocked by a network problem.</summary>
    public static Snapshot Evaluate(string? token, string? publicKey, DateTime? checkedAt, DateTime utcNow)
    {
        LicenseGrant? grant = null;
        if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(publicKey))
        {
            using var key = LicenseToken.ImportPublicKey(publicKey);
            grant = LicenseToken.Verify(token, key);
        }

        if (grant is null)
        {
            return new Snapshot(true, LicenseStatus.Grace, "Abbonamento non ancora verificato con la console: il server deve poter raggiungere internet.",
                null, null, null, null, checkedAt, null);
        }

        var (status, message) = LicenseToken.Effective(grant, utcNow);
        return new Snapshot(true, status, message, grant.Plan, grant.Modules, grant.MaxUsers, grant.ValidUntil, checkedAt, grant.Customer);
    }

    /// <summary>What a suspended installation still answers: login and account, support, the license itself,
    /// and a limited overall view (dashboard, lists). No detail opens and nothing changes.</summary>
    public static bool IsAllowedWhenSuspended(HttpRequest request)
    {
        var path = request.Path;
        if (path.StartsWithSegments("/api/auth") || path.StartsWithSegments("/api/account") || path.StartsWithSegments("/api/support")
            || path.StartsWithSegments("/api/license") || !path.StartsWithSegments("/api"))
        {
            return true;
        }

        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var value = path.Value?.TrimEnd('/').ToLowerInvariant();
        return value is "/api/company-profile" or "/api/company-profile/areas" or "/api/company-profile/access"
            or "/api/work-orders" or "/api/work-orders/dashboard" or "/api/work-orders/lookup" or "/api/materials";
    }
}

/// <summary>Every hour (the first time a minute after start) tells the console this installation is alive
/// and what it uses, and receives the signed license. The console's public key is pinned at the first
/// successful contact: a later answer signed by another key is refused.</summary>
public sealed class LicenseHeartbeatService : BackgroundService
{
    public const string HttpClientName = "license";
    public const string KeyHeader = "X-License-Key";
    private readonly LicenseState _state;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<LicenseHeartbeatService> _logger;

    public LicenseHeartbeatService(LicenseState state, IServiceScopeFactory scopes, IHttpClientFactory http, ILogger<LicenseHeartbeatService> logger)
    {
        _state = state;
        _scopes = scopes;
        _http = http;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_state.Enabled)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckNowAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>One contact with the console; true when a valid license was received and stored.</summary>
    public async Task<bool> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_state.Enabled)
        {
            return false;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var profile = await db.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
            long? size = db.Database.IsNpgsql()
                ? await db.Database.SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"").SingleAsync(cancellationToken)
                : null;
            var heartbeat = new Heartbeat(
                SupportController.ServerVersion,
                ServerConfiguration.HostingMode(scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue<bool>(SupportController.ServerConfigLoadedKey)),
                profile?.CompanyName,
                profile?.VatNumber,
                profile is null ? [] : Sectors.Parse(profile.EnabledModules).ToList(),
                await db.Users.CountAsync(u => u.IsActive, cancellationToken),
                size,
                true,
                DateTime.UtcNow);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_state.ConsoleUrl}/api/installations/heartbeat")
            {
                Content = JsonContent.Create(heartbeat),
            };
            request.Headers.Add(KeyHeader, _state.Key);
            using var response = await _http.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Console licenze: risposta {Status}", (int)response.StatusCode);
                return false;
            }

            var reply = await response.Content.ReadFromJsonAsync<HeartbeatReply>(cancellationToken);
            if (reply is null || profile is null)
            {
                return false;
            }

            if (profile.LicensePublicKey is not null && profile.LicensePublicKey != reply.PublicKey)
            {
                _logger.LogError("Console licenze: chiave pubblica diversa da quella registrata, licenza rifiutata");
                return false;
            }

            using var key = LicenseToken.ImportPublicKey(reply.PublicKey);
            if (LicenseToken.Verify(reply.License, key) is null)
            {
                _logger.LogError("Console licenze: firma della licenza non valida");
                return false;
            }

            profile.LicensePublicKey ??= reply.PublicKey;
            profile.LicenseToken = reply.License;
            profile.LicenseCheckedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            _state.Invalidate();
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or FormatException)
        {
            _logger.LogWarning(exception, "Console licenze non raggiungibile");
            return false;
        }
    }
}
