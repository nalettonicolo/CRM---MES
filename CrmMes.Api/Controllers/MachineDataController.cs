using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Machine interconnection. Machines (or the gateway that reads them) push readings with their
/// own token in the "X-Machine-Token" header, no user login involved; people read the resulting
/// figures. Tokens are generated and revoked by an Admin and shown only once.</summary>
[ApiController]
public class MachineDataController : ControllerBase
{
    public const string TokenHeader = "X-Machine-Token";
    private const int MaxBatch = 500;
    private readonly ApplicationDbContext _dbContext;

    public MachineDataController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Readings from a machine: one or a batch (a gateway that was offline sends what it kept).
    /// Duplicates (same machine and timestamp) are ignored, so a resend is harmless.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Machine)]
    [HttpPost("api/machine-data/{equipmentId:guid}")]
    public async Task<ActionResult<MachineIngestResponse>> Ingest(
        Guid equipmentId, List<MachineReadingRequest> readings, CancellationToken cancellationToken = default)
    {
        var connection = await _dbContext.MachineConnections.FirstOrDefaultAsync(c => c.EquipmentId == equipmentId, cancellationToken);
        var token = Request.Headers[TokenHeader].ToString();
        if (connection is null || string.IsNullOrEmpty(token) || !HashMatches(token, connection.TokenHash))
        {
            return Unauthorized(new { message = "Token macchina non valido." });
        }

        if (readings.Count == 0 || readings.Count > MaxBatch)
        {
            return BadRequest(new { message = $"Da 1 a {MaxBatch} letture per invio." });
        }

        var now = DateTime.UtcNow;
        var accepted = new List<MachineEvent>();
        foreach (var (reading, index) in readings.Select((r, i) => (r, i)))
        {
            var state = MachineStats.States.FirstOrDefault(s => string.Equals(s, reading.State?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (state is null)
            {
                return BadRequest(new { message = $"Lettura {index + 1}: stato non valido ({string.Join(", ", MachineStats.States)})." });
            }

            var timestamp = (reading.Timestamp ?? now).ToUniversalTime();
            if (timestamp > now.AddMinutes(5) || timestamp < now.AddDays(-30))
            {
                return BadRequest(new { message = $"Lettura {index + 1}: orario fuori intervallo (ultimi 30 giorni)." });
            }

            if (reading.PieceCounter < 0 || reading.ScrapCounter < 0)
            {
                return BadRequest(new { message = $"Lettura {index + 1}: contatori negativi." });
            }

            if (reading.EnergyKwh < 0)
            {
                return BadRequest(new { message = $"Lettura {index + 1}: energia negativa." });
            }

            accepted.Add(new MachineEvent
            {
                EquipmentId = equipmentId,
                Timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
                State = state,
                PieceCounter = reading.PieceCounter,
                ScrapCounter = reading.ScrapCounter,
                AlarmCode = Clip(reading.AlarmCode, 50),
                AlarmText = Clip(reading.AlarmText, 200),
                WorkOrderCode = Clip(reading.WorkOrderCode, 50),
                EnergyKwh = reading.EnergyKwh,
                ReceivedAt = now
            });
        }

        var timestamps = accepted.Select(e => e.Timestamp).ToList();
        var existing = (await _dbContext.MachineEvents
                .Where(e => e.EquipmentId == equipmentId && timestamps.Contains(e.Timestamp))
                .Select(e => e.Timestamp)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var fresh = accepted.Where(e => !existing.Contains(e.Timestamp)).GroupBy(e => e.Timestamp).Select(g => g.Last()).ToList();
        _dbContext.MachineEvents.AddRange(fresh);

        var latest = accepted.MaxBy(e => e.Timestamp)!;
        if (connection.LastSeenAt is null || latest.Timestamp >= connection.LastSeenAt)
        {
            connection.LastSeenAt = latest.Timestamp;
            connection.LastState = latest.State;
            connection.LastPieceCounter = latest.PieceCounter ?? connection.LastPieceCounter;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new MachineIngestResponse(fresh.Count, accepted.Count - fresh.Count));
    }

    /// <summary>Creates (or replaces, revoking the old one) the machine's token. The plain token is in
    /// this response only: it is stored as a hash and can't be read back.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("api/equipment/{equipmentId:guid}/machine-token")]
    public async Task<ActionResult<MachineTokenResponse>> CreateToken(Guid equipmentId, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Equipment.AnyAsync(e => e.Id == equipmentId, cancellationToken))
        {
            return NotFound();
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var connection = await _dbContext.MachineConnections.FirstOrDefaultAsync(c => c.EquipmentId == equipmentId, cancellationToken);
        if (connection is null)
        {
            connection = new MachineConnection { EquipmentId = equipmentId };
            _dbContext.MachineConnections.Add(connection);
        }

        connection.TokenHash = Hash(token);
        connection.TokenPrefix = token[..6];
        connection.CreatedAt = DateTime.UtcNow;
        connection.CreatedBy = User.FindFirstValue(ClaimTypes.Name);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "MachineTokenCreated",
            EntityType = "Equipment",
            EntityId = equipmentId,
            UserName = connection.CreatedBy,
            Details = $"Nuovo token di collegamento macchina ({connection.TokenPrefix}...)."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        var endpoint = $"{Request.Scheme}://{Request.Host}/api/machine-data/{equipmentId}";
        return Ok(new MachineTokenResponse(token, connection.TokenPrefix, endpoint, TokenHeader));
    }

    /// <summary>Generates a short burst of machine readings for demos and training, without a gateway or token.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("api/equipment/{equipmentId:guid}/demo-feed")]
    public async Task<ActionResult<MachineDemoFeedResponse>> DemoFeed(
        Guid equipmentId, [FromQuery] int seconds = 60, CancellationToken cancellationToken = default)
    {
        if (seconds is < 1 or > 3600)
        {
            return BadRequest(new { message = "Durata demo tra 1 e 3600 secondi." });
        }

        if (!await _dbContext.Equipment.AnyAsync(e => e.Id == equipmentId, cancellationToken))
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var anchor = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var step = Math.Max(1, seconds / 12);
        var planned = new List<MachineEvent>();
        decimal energy = 50m;
        var cycle = new[] { "Running", "Running", "Idle", "Running", "Setup", "Running" };
        for (int i = 0, offset = seconds; offset >= 0; offset -= step, i++)
        {
            var state = cycle[i % cycle.Length];
            if (state == "Running")
            {
                energy += 0.08m + i * 0.01m;
            }
            else if (state == "Idle")
            {
                energy += 0.02m;
            }

            planned.Add(new MachineEvent
            {
                EquipmentId = equipmentId,
                Timestamp = anchor.AddSeconds(-offset),
                State = state,
                // No piece/scrap counters: demo must not pollute shared OEE quality aggregates in tests/prod.
                PieceCounter = null,
                ScrapCounter = null,
                EnergyKwh = Math.Round(energy, 3),
                ReceivedAt = now
            });
        }

        var timestamps = planned.Select(e => e.Timestamp).ToList();
        var existing = (await _dbContext.MachineEvents
                .Where(e => e.EquipmentId == equipmentId && timestamps.Contains(e.Timestamp))
                .Select(e => e.Timestamp)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var fresh = planned.Where(e => !existing.Contains(e.Timestamp)).ToList();
        if (fresh.Count > 0)
        {
            _dbContext.MachineEvents.AddRange(fresh);
            var connection = await _dbContext.MachineConnections.FirstOrDefaultAsync(c => c.EquipmentId == equipmentId, cancellationToken);
            var latest = fresh.MaxBy(e => e.Timestamp)!;
            if (connection is not null && (connection.LastSeenAt is null || latest.Timestamp >= connection.LastSeenAt))
            {
                connection.LastSeenAt = latest.Timestamp;
                connection.LastState = latest.State;
                connection.LastPieceCounter = latest.PieceCounter;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new MachineDemoFeedResponse(fresh.Count, planned.Count - fresh.Count));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("api/equipment/{equipmentId:guid}/machine-token")]
    public async Task<IActionResult> RevokeToken(Guid equipmentId, CancellationToken cancellationToken = default)
    {
        var connection = await _dbContext.MachineConnections.FirstOrDefaultAsync(c => c.EquipmentId == equipmentId, cancellationToken);
        if (connection is null)
        {
            return NotFound();
        }

        _dbContext.MachineConnections.Remove(connection);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "MachineTokenRevoked",
            EntityType = "Equipment",
            EntityId = equipmentId,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Token di collegamento macchina revocato ({connection.TokenPrefix}...)."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>A machine's day (Italian calendar day): minutes per state, pieces, availability, alarms.</summary>
    [Authorize]
    [HttpGet("api/equipment/{equipmentId:guid}/machine-day")]
    public async Task<ActionResult<MachineDayResponse>> GetDay(Guid equipmentId, [FromQuery] DateTime? date = null, CancellationToken cancellationToken = default)
    {
        var equipment = await _dbContext.Equipment.AsNoTracking().FirstOrDefaultAsync(e => e.Id == equipmentId, cancellationToken);
        if (equipment is null)
        {
            return NotFound();
        }

        var connection = await _dbContext.MachineConnections.AsNoTracking().FirstOrDefaultAsync(c => c.EquipmentId == equipmentId, cancellationToken);
        var day = (date ?? ItalianNow()).Date;
        var from = ItalianMidnightToUtc(day);
        var to = ItalianMidnightToUtc(day.AddDays(1));
        var end = to > DateTime.UtcNow ? DateTime.UtcNow : to;
        var events = await _dbContext.MachineEvents.AsNoTracking()
            .Where(e => e.EquipmentId == equipmentId && e.Timestamp >= from.AddHours(-12) && e.Timestamp < to)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);
        var stats = MachineStats.Compute(events, from, end > from ? end : from);
        var alarms = events.Where(e => e.Timestamp >= from && e.State == "Alarm" && (e.AlarmCode != null || e.AlarmText != null))
            .OrderByDescending(e => e.Timestamp).Take(20)
            .Select(e => new MachineAlarmResponse(e.Timestamp, e.AlarmCode, e.AlarmText)).ToList();
        return Ok(new MachineDayResponse(
            equipmentId, equipment.Name, day, connection is not null, connection?.TokenPrefix, connection?.LastSeenAt, connection?.LastState,
            stats.MinutesByState.ToDictionary(p => p.Key, p => p.Value), stats.NoDataMinutes, stats.Pieces, stats.Scrap,
            stats.Availability, stats.AlarmCount, alarms));
    }

    /// <summary>All connected machines with their last state, for an at-a-glance view of the floor.</summary>
    [Authorize]
    [HttpGet("api/machine-data/overview")]
    public async Task<ActionResult<IEnumerable<MachineOverviewResponse>>> Overview(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var rows = await _dbContext.MachineConnections.AsNoTracking()
            .Select(c => new { c.EquipmentId, c.Equipment.Name, c.Equipment.Code, c.LastSeenAt, c.LastState, c.LastPieceCounter })
            .ToListAsync(cancellationToken);
        return Ok(rows.OrderBy(r => r.Name).Select(r => new MachineOverviewResponse(
            r.EquipmentId, r.Name, r.Code, r.LastSeenAt,
            r.LastSeenAt is { } seen && now - seen <= MachineStats.MaxGap && r.LastState is not null ? r.LastState : "NoData",
            r.LastPieceCounter)));
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool HashMatches(string token, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(storedHash));

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];

    private static TimeZoneInfo? Rome()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
    }

    private static DateTime ItalianNow() => Rome() is { } zone ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone) : DateTime.UtcNow;

    private static DateTime ItalianMidnightToUtc(DateTime day)
    {
        var midnight = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
        return Rome() is { } zone ? TimeZoneInfo.ConvertTimeToUtc(midnight, zone) : DateTime.SpecifyKind(midnight, DateTimeKind.Utc);
    }
}

public sealed record MachineReadingRequest(
    DateTime? Timestamp, string? State, long? PieceCounter, long? ScrapCounter, string? AlarmCode, string? AlarmText, string? WorkOrderCode,
    decimal? EnergyKwh = null);

public sealed record MachineIngestResponse(int Accepted, int Duplicates);

public sealed record MachineTokenResponse(string Token, string TokenPrefix, string Endpoint, string Header);

public sealed record MachineAlarmResponse(DateTime Timestamp, string? Code, string? Text);

public sealed record MachineDayResponse(
    Guid EquipmentId, string EquipmentName, DateTime Day, bool Connected, string? TokenPrefix, DateTime? LastSeenAt, string? LastState,
    Dictionary<string, decimal> MinutesByState, decimal NoDataMinutes, long Pieces, long Scrap, decimal? Availability, int AlarmCount,
    List<MachineAlarmResponse> Alarms);

public sealed record MachineOverviewResponse(Guid EquipmentId, string Name, string Code, DateTime? LastSeenAt, string LastState, long? LastPieceCounter);

public sealed record MachineDemoFeedResponse(int Created, int SkippedDuplicates);
