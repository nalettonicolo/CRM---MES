using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>HACCP monitoring registers (Reg. CE 852/2004): the critical control points of the plan and
/// their readings. Anyone on the floor records a reading; out of limits it must state the corrective
/// action. Readings are never edited or deleted — the register is evidence for inspections — and a
/// control point is deactivated, not deleted, so its history stays.</summary>
[ApiController]
[Authorize]
[Route("api/haccp")]
public class HaccpController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public HaccpController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("control-points")]
    public async Task<ActionResult<IEnumerable<HaccpControlPointResponse>>> GetControlPoints(
        [FromQuery] bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.HaccpControlPoints.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(point => point.IsActive);
        }

        var points = await query.OrderBy(point => point.Location).ThenBy(point => point.Name).ToListAsync(cancellationToken);
        var ids = points.Select(point => point.Id).ToList();
        var last = (await _dbContext.HaccpReadings.AsNoTracking()
                .Where(reading => ids.Contains(reading.ControlPointId))
                .ToListAsync(cancellationToken))
            .GroupBy(reading => reading.ControlPointId)
            .ToDictionary(group => group.Key, group => group.MaxBy(reading => reading.ReadAt)!);
        return Ok(points.Select(point => ToResponse(point, last.GetValueOrDefault(point.Id))));
    }

    /// <summary>Models of control points by type of process, to start the plan from.</summary>
    [HttpGet("templates")]
    public ActionResult<IEnumerable<HaccpTemplateResponse>> GetTemplates() =>
        Ok(HaccpTemplates.All.Select(template => new HaccpTemplateResponse(
            template.Key, template.Name, template.Description, template.Points)));

    /// <summary>Adds the points of a model to the register. A point whose name is already active is left
    /// alone, so applying the same model twice creates nothing new.</summary>
    [Authorize(Policy = "Warehouse")]
    [HttpPost("templates/{key}/apply")]
    public async Task<ActionResult<ApplyHaccpTemplateResponse>> ApplyTemplate(
        string key, CancellationToken cancellationToken = default)
    {
        var template = HaccpTemplates.Find(key);
        if (template is null)
        {
            return NotFound(new { message = "Modello HACCP non trovato." });
        }

        var existing = (await _dbContext.HaccpControlPoints.AsNoTracking()
                .Where(point => point.IsActive)
                .Select(point => point.Name)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = 0;
        foreach (var model in template.Points)
        {
            if (!existing.Add(model.Name))
            {
                continue;
            }

            _dbContext.HaccpControlPoints.Add(new HaccpControlPoint
            {
                Name = model.Name,
                Location = model.Location,
                Hazard = model.Hazard,
                Unit = model.Unit,
                MinValue = model.MinValue,
                MaxValue = model.MaxValue,
                Frequency = model.Frequency,
                CorrectiveActionHint = model.CorrectiveActionHint
            });
            created++;
        }

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "HaccpTemplateApplied",
            EntityType = "HaccpControlPoint",
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Modello HACCP '{template.Name}': {created} punti aggiunti, {template.Points.Count - created} già presenti."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new ApplyHaccpTemplateResponse(template.Key, created, template.Points.Count - created));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("control-points")]
    public async Task<ActionResult<HaccpControlPointResponse>> CreateControlPoint(
        SaveHaccpControlPointRequest request, CancellationToken cancellationToken = default)
    {
        var point = new HaccpControlPoint();
        if (Apply(point, request) is { } error)
        {
            return error;
        }

        _dbContext.HaccpControlPoints.Add(point);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(point, null));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPut("control-points/{id:guid}")]
    public async Task<ActionResult<HaccpControlPointResponse>> UpdateControlPoint(
        Guid id, SaveHaccpControlPointRequest request, CancellationToken cancellationToken = default)
    {
        var point = await _dbContext.HaccpControlPoints.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (point is null)
        {
            return NotFound();
        }

        if (Apply(point, request) is { } error)
        {
            return error;
        }

        point.IsActive = request.IsActive ?? point.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(point, null));
    }

    /// <summary>Records a reading. A numeric point needs a value, judged against its limits; a yes/no
    /// point needs the outcome. When not compliant the corrective action is mandatory.</summary>
    [HttpPost("control-points/{id:guid}/readings")]
    public async Task<ActionResult<HaccpReadingResponse>> AddReading(
        Guid id, AddHaccpReadingRequest request, CancellationToken cancellationToken = default)
    {
        var point = await _dbContext.HaccpControlPoints.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (point is null)
        {
            return NotFound();
        }

        if (!point.IsActive)
        {
            return Conflict(new { message = "Punto di controllo disattivato." });
        }

        bool compliant;
        if (IsNumeric(point))
        {
            if (request.Value is not { } value)
            {
                return BadRequest(new { message = $"Indica il valore misurato ({point.Unit ?? "valore"})." });
            }

            compliant = (point.MinValue is null || value >= point.MinValue) && (point.MaxValue is null || value <= point.MaxValue);
        }
        else
        {
            if (request.Compliant is not { } outcome)
            {
                return BadRequest(new { message = "Indica se il controllo è conforme." });
            }

            compliant = outcome;
        }

        var action = string.IsNullOrWhiteSpace(request.CorrectiveAction) ? null : request.CorrectiveAction.Trim();
        if (!compliant && action is null)
        {
            return BadRequest(new { message = "Valore fuori limite: descrivi l'azione correttiva." });
        }

        var readAt = request.ReadAt ?? DateTime.UtcNow;
        if (readAt > DateTime.UtcNow.AddMinutes(5))
        {
            return BadRequest(new { message = "La lettura non può essere nel futuro." });
        }

        var reading = new HaccpReading
        {
            ControlPointId = point.Id,
            Value = IsNumeric(point) ? request.Value : null,
            Compliant = compliant,
            CorrectiveAction = action,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ReadAt = readAt,
            OperatorName = User.FindFirstValue(ClaimTypes.Name)
        };
        _dbContext.HaccpReadings.Add(reading);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(reading, point));
    }

    /// <summary>The register for a period (inclusive days), optionally one point or only non-compliant.</summary>
    [HttpGet("readings")]
    public async Task<ActionResult<IEnumerable<HaccpReadingResponse>>> GetReadings(
        [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] Guid? controlPointId = null,
        [FromQuery] bool nonCompliantOnly = false, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return BadRequest(new { message = "La data finale precede quella iniziale." });
        }

        var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);
        var query = _dbContext.HaccpReadings.AsNoTracking().Include(r => r.ControlPoint)
            .Where(r => r.ReadAt >= fromUtc && r.ReadAt < toUtc);
        if (controlPointId.HasValue)
        {
            query = query.Where(r => r.ControlPointId == controlPointId);
        }

        if (nonCompliantOnly)
        {
            query = query.Where(r => !r.Compliant);
        }

        var readings = await query.OrderByDescending(r => r.ReadAt).Take(5000).ToListAsync(cancellationToken);
        return Ok(readings.Select(r => ToResponse(r, r.ControlPoint)));
    }

    private ActionResult? Apply(HaccpControlPoint point, SaveHaccpControlPointRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Indica il nome del punto di controllo." });
        }

        if (request.MinValue is { } min && request.MaxValue is { } max && min > max)
        {
            return BadRequest(new { message = "Il limite minimo supera il massimo." });
        }

        point.Name = request.Name.Trim();
        point.Location = Clean(request.Location);
        point.Hazard = Clean(request.Hazard);
        point.Unit = Clean(request.Unit);
        point.MinValue = request.MinValue;
        point.MaxValue = request.MaxValue;
        point.Frequency = Clean(request.Frequency);
        point.CorrectiveActionHint = Clean(request.CorrectiveActionHint);
        return null;
    }

    private static bool IsNumeric(HaccpControlPoint point) => point.MinValue.HasValue || point.MaxValue.HasValue;

    private static HaccpControlPointResponse ToResponse(HaccpControlPoint point, HaccpReading? last) => new(
        point.Id, point.Name, point.Location, point.Hazard, point.Unit, point.MinValue, point.MaxValue, point.Frequency,
        point.CorrectiveActionHint, point.IsActive, IsNumeric(point), last?.ReadAt, last?.Value, last?.Compliant);

    private static HaccpReadingResponse ToResponse(HaccpReading reading, HaccpControlPoint point) => new(
        reading.Id, point.Id, point.Name, point.Location, point.Unit, point.MinValue, point.MaxValue,
        reading.Value, reading.Compliant, reading.CorrectiveAction, reading.Notes, reading.ReadAt, reading.OperatorName);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SaveHaccpControlPointRequest(
    string? Name, string? Location, string? Hazard, string? Unit, decimal? MinValue, decimal? MaxValue,
    string? Frequency, string? CorrectiveActionHint, bool? IsActive = null);

public sealed record HaccpControlPointResponse(
    Guid Id, string Name, string? Location, string? Hazard, string? Unit, decimal? MinValue, decimal? MaxValue,
    string? Frequency, string? CorrectiveActionHint, bool IsActive, bool IsNumeric,
    DateTime? LastReadAt, decimal? LastValue, bool? LastCompliant);

public sealed record HaccpTemplateResponse(
    string Key, string Name, string Description, IReadOnlyList<HaccpTemplates.Point> Points);

public sealed record ApplyHaccpTemplateResponse(string Key, int Created, int Skipped);

public sealed record AddHaccpReadingRequest(decimal? Value, bool? Compliant, string? CorrectiveAction, string? Notes, DateTime? ReadAt);

public sealed record HaccpReadingResponse(
    Guid Id, Guid ControlPointId, string ControlPointName, string? Location, string? Unit, decimal? MinValue,
    decimal? MaxValue, decimal? Value, bool Compliant, string? CorrectiveAction, string? Notes, DateTime ReadAt,
    string? OperatorName);
