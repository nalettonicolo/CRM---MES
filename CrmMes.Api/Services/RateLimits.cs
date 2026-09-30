using System.Security.Claims;
using System.Threading.RateLimiting;

namespace CrmMes.Api.Services;

/// <summary>Request limits on the endpoints that accept guesses. The API is public on the internet:
/// "Auth" (login, refresh, register) is limited per client IP, so a password can't be tried at will even
/// across many accounts (the per-account lockout in AuthController covers a single account);
/// "Pin" (terminal identification) is limited per logged-in user, since a 4-digit PIN has only 10,000
/// values and each attempt hashes against every user's PIN.
/// Limits come from configuration ("RateLimits:AuthPerMinute", "RateLimits:PinPerMinute").</summary>
public static class RateLimits
{
    public const string Auth = "auth";
    public const string Pin = "pin";

    /// <summary>Machine data: per machine and address, generous enough for a reading every second in
    /// bursts, tight enough that a stolen token can't flood the database.</summary>
    public const string Machine = "machine";

    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        var authPerMinute = configuration.GetValue("RateLimits:AuthPerMinute", 20);
        var pinPerMinute = configuration.GetValue("RateLimits:PinPerMinute", 10);
        var machinePerMinute = configuration.GetValue("RateLimits:MachinePerMinute", 120);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"message\":\"Troppe richieste in poco tempo: riprova tra un minuto.\"}", cancellationToken);
            };

            options.AddPolicy(Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.AddPolicy(Pin, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.User.FindFirstValue("sub")
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = pinPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.AddPolicy(Machine, context => RateLimitPartition.GetFixedWindowLimiter(
                $"{context.Request.RouteValues["equipmentId"]}|{context.Connection.RemoteIpAddress}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = machinePerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }
}
