using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/instruments")]
public class InstrumentsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public InstrumentsController(ApplicationDbContext dbContext) => _dbContext = dbContext;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MeasuringInstrumentResponse>>> GetInstruments(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.MeasuringInstruments.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(i => i.IsActive);
        }

        var items = await query
            .OrderBy(i => i.Code)
            .Select(i => ToResponse(i))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MeasuringInstrumentDetailResponse>> GetInstrument(Guid id, CancellationToken cancellationToken = default)
    {
        var instrument = await _dbContext.MeasuringInstruments.AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (instrument is null)
        {
            return NotFound();
        }

        var calibrations = await _dbContext.InstrumentCalibrations.AsNoTracking()
            .Where(c => c.InstrumentId == id)
            .OrderByDescending(c => c.CalibratedAt)
            .Take(20)
            .Select(c => new InstrumentCalibrationResponse(
                c.Id, c.InstrumentId, c.CalibratedAt, c.NextDue, c.Result, c.CertificateNumber, c.Notes, c.PerformedBy))
            .ToListAsync(cancellationToken);

        return Ok(new MeasuringInstrumentDetailResponse(
            instrument.Id, instrument.Code, instrument.Name, instrument.SerialNumber,
            instrument.NextCalibrationDue, instrument.LastCalibrationAt, instrument.CalibrationIntervalDays,
            instrument.IsActive, instrument.Notes, calibrations));
    }

    [Authorize(Policy = "Engineering")]
    [HttpPost]
    public async Task<ActionResult<MeasuringInstrumentResponse>> CreateInstrument(
        CreateMeasuringInstrumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code?.Trim();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Codice e nome sono obbligatori." });
        }

        if (await _dbContext.MeasuringInstruments.AnyAsync(i => i.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Esiste già uno strumento con questo codice." });
        }

        var interval = request.CalibrationIntervalDays is > 0 ? request.CalibrationIntervalDays.Value : 365;
        var instrument = new MeasuringInstrument
        {
            Code = code,
            Name = name,
            SerialNumber = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim(),
            CalibrationIntervalDays = interval,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            NextCalibrationDue = request.NextCalibrationDue
        };

        _dbContext.MeasuringInstruments.Add(instrument);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created($"api/instruments/{instrument.Id}", ToResponse(instrument));
    }

    [Authorize(Policy = "Engineering")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MeasuringInstrumentResponse>> UpdateInstrument(
        Guid id,
        UpdateMeasuringInstrumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var instrument = await _dbContext.MeasuringInstruments.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (instrument is null)
        {
            return NotFound();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Il nome è obbligatorio." });
        }

        instrument.Name = name;
        instrument.SerialNumber = string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim();
        if (request.CalibrationIntervalDays is > 0)
        {
            instrument.CalibrationIntervalDays = request.CalibrationIntervalDays.Value;
        }

        instrument.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        instrument.NextCalibrationDue = request.NextCalibrationDue;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(instrument));
    }

    [Authorize(Policy = "Engineering")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateInstrument(Guid id, CancellationToken cancellationToken = default)
    {
        var instrument = await _dbContext.MeasuringInstruments.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (instrument is null)
        {
            return NotFound();
        }

        instrument.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "Engineering")]
    [HttpPost("{id:guid}/calibrations")]
    public async Task<ActionResult<InstrumentCalibrationResponse>> RecordCalibration(
        Guid id,
        RecordCalibrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var instrument = await _dbContext.MeasuringInstruments.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (instrument is null)
        {
            return NotFound();
        }

        var result = string.IsNullOrWhiteSpace(request.Result) ? CalibrationResults.Pass : request.Result.Trim();
        if (result is not CalibrationResults.Pass and not CalibrationResults.Fail)
        {
            return BadRequest(new { message = "Esito calibrazione non valido (Pass o Fail)." });
        }

        var calibratedAt = request.CalibratedAt ?? DateTime.UtcNow;
        var nextDue = request.NextDue
            ?? calibratedAt.Date.AddDays(instrument.CalibrationIntervalDays);

        var calibration = new InstrumentCalibration
        {
            InstrumentId = id,
            CalibratedAt = calibratedAt,
            NextDue = nextDue,
            Result = result,
            CertificateNumber = string.IsNullOrWhiteSpace(request.CertificateNumber) ? null : request.CertificateNumber.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            PerformedBy = User.FindFirstValue(ClaimTypes.Name)
        };

        _dbContext.InstrumentCalibrations.Add(calibration);
        instrument.LastCalibrationAt = calibratedAt;
        instrument.NextCalibrationDue = nextDue;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new InstrumentCalibrationResponse(
            calibration.Id, calibration.InstrumentId, calibration.CalibratedAt, calibration.NextDue,
            calibration.Result, calibration.CertificateNumber, calibration.Notes, calibration.PerformedBy));
    }

    private static MeasuringInstrumentResponse ToResponse(MeasuringInstrument i) =>
        new(i.Id, i.Code, i.Name, i.SerialNumber, i.NextCalibrationDue, i.LastCalibrationAt,
            i.CalibrationIntervalDays, i.IsActive, i.Notes);
}

public sealed record MeasuringInstrumentResponse(
    Guid Id, string Code, string Name, string? SerialNumber, DateTime? NextCalibrationDue,
    DateTime? LastCalibrationAt, int CalibrationIntervalDays, bool IsActive, string? Notes);

public sealed record MeasuringInstrumentDetailResponse(
    Guid Id, string Code, string Name, string? SerialNumber, DateTime? NextCalibrationDue,
    DateTime? LastCalibrationAt, int CalibrationIntervalDays, bool IsActive, string? Notes,
    List<InstrumentCalibrationResponse> Calibrations);

public sealed record CreateMeasuringInstrumentRequest(
    string? Code, string? Name, string? SerialNumber, int? CalibrationIntervalDays, DateTime? NextCalibrationDue, string? Notes);

public sealed record UpdateMeasuringInstrumentRequest(
    string? Name, string? SerialNumber, int? CalibrationIntervalDays, DateTime? NextCalibrationDue, string? Notes);

public sealed record RecordCalibrationRequest(
    DateTime? CalibratedAt, DateTime? NextDue, string? Result, string? CertificateNumber, string? Notes);

public sealed record InstrumentCalibrationResponse(
    Guid Id, Guid InstrumentId, DateTime CalibratedAt, DateTime NextDue, string Result,
    string? CertificateNumber, string? Notes, string? PerformedBy);
