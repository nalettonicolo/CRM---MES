using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Remote assistance. "info" is what the client's support window shows even before anyone can log
/// in (who to call, which remote-control tool to start): nothing in it is secret, a RustDesk key is the
/// relay's public key. "diagnostics" is the state of the server an assistant needs to look at from afar,
/// for administrators only and without any secret (no connection string, no key, no user data).</summary>
[ApiController]
[Route("api/support")]
public class SupportController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public SupportController(ApplicationDbContext dbContext, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _environment = environment;
    }

    public static string ServerVersion =>
        typeof(SupportController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(SupportController).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    [AllowAnonymous]
    [HttpGet("info")]
    public ActionResult<SupportInfoResponse> Info()
    {
        var section = _configuration.GetSection("Support");
        return Ok(new SupportInfoResponse(
            Clean(section["Name"]),
            Clean(section["Email"]),
            Clean(section["Phone"]),
            Clean(section["Hours"]),
            Clean(section["RustDeskIdServer"]),
            Clean(section["RustDeskKey"]),
            Clean(section["RemoteToolUrl"]),
            ServerVersion,
            ServerConfiguration.HostingMode(ConfigFileLoaded)));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("diagnostics")]
    public async Task<ActionResult<SupportDiagnosticsResponse>> Diagnostics(CancellationToken cancellationToken = default)
    {
        var database = await DatabaseDiagnosticsAsync(cancellationToken);
        var startedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var logsPath = Clean(_configuration["Logs:Path"]);

        return Ok(new SupportDiagnosticsResponse(
            ServerVersion,
            ServerConfiguration.HostingMode(ConfigFileLoaded),
            _environment.EnvironmentName,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            DateTime.UtcNow,
            startedAt,
            Math.Round((DateTime.UtcNow - startedAt).TotalHours, 1),
            ConfigFileLoaded,
            logsPath,
            FreeDiskGigabytes(AppContext.BaseDirectory),
            logsPath is null ? null : FreeDiskGigabytes(logsPath),
            database));
    }

    private bool ConfigFileLoaded => _configuration.GetValue<bool>(ServerConfigLoadedKey);

    /// <summary>Set by Program.cs when the customer's settings file was found and loaded.</summary>
    public const string ServerConfigLoadedKey = "Server:ConfigFileLoaded";

    private async Task<DatabaseDiagnostics> DatabaseDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var provider = _dbContext.Database.ProviderName ?? "sconosciuto";
        bool canConnect;
        try
        {
            canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            canConnect = false;
        }

        if (!canConnect)
        {
            return new DatabaseDiagnostics(provider, false, null, null, [], null, null, null);
        }

        int? appliedCount = null;
        string? lastApplied = null;
        List<string> pending = [];
        if (_dbContext.Database.IsRelational() && _dbContext.Database.GetMigrations().Any())
        {
            try
            {
                var applied = (await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
                appliedCount = applied.Count;
                lastApplied = applied.LastOrDefault();
                pending = (await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            }
            catch
            {
                // A database created without migrations (the test suite's) has no history table.
            }
        }

        long? sizeBytes = null;
        if (_dbContext.Database.IsNpgsql())
        {
            sizeBytes = await _dbContext.Database
                .SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"")
                .SingleAsync(cancellationToken);
        }

        var activeUsers = await _dbContext.Users.CountAsync(u => u.IsActive, cancellationToken);
        var openWorkOrders = await _dbContext.WorkOrders.CountAsync(
            w => w.Status == "Released" || w.Status == "InProgress", cancellationToken);

        return new DatabaseDiagnostics(provider, true, appliedCount, lastApplied, pending, sizeBytes, activeUsers, openWorkOrders);
    }

    private static double? FreeDiskGigabytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return root is null ? null : Math.Round(new DriveInfo(root).AvailableFreeSpace / 1_073_741_824d, 1);
        }
        catch
        {
            return null;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SupportInfoResponse(
    string? Name,
    string? Email,
    string? Phone,
    string? Hours,
    string? RustDeskIdServer,
    string? RustDeskKey,
    string? RemoteToolUrl,
    string ServerVersion,
    string Hosting);

public sealed record DatabaseDiagnostics(
    string Provider,
    bool CanConnect,
    int? AppliedMigrations,
    string? LastMigration,
    List<string> PendingMigrations,
    long? SizeBytes,
    int? ActiveUsers,
    int? OpenWorkOrders);

public sealed record SupportDiagnosticsResponse(
    string ServerVersion,
    string Hosting,
    string Environment,
    string MachineName,
    string OperatingSystem,
    string Runtime,
    DateTime ServerTimeUtc,
    DateTime StartedAtUtc,
    double UptimeHours,
    bool ConfigFileLoaded,
    string? LogsPath,
    double? FreeDiskGigabytes,
    double? FreeLogDiskGigabytes,
    DatabaseDiagnostics Database);
