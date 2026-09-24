using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Misurazioni registrate contro i checkpoint di QualityCheckpointsController per una commessa
/// — i dati dietro un certificato di conformità. Non è un vero SPC con carte di controllo/Cp-Cpk: solo
/// pass/fail per tolleranza e uno storico di cosa è stato misurato.</summary>
[ApiController]
[Authorize]
[Route("api/quality-measurements")]
public class QualityMeasurementsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public QualityMeasurementsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<QualityMeasurementResponse>>> GetMeasurements(
        [FromQuery] Guid workOrderId,
        CancellationToken cancellationToken = default)
    {
        var measurements = await _dbContext.QualityMeasurements.AsNoTracking()
            .Include(m => m.Checkpoint)
            .Include(m => m.WorkOrderUnit)
            .Where(m => m.WorkOrderId == workOrderId)
            .OrderByDescending(m => m.MeasuredAt)
            .Select(m => new QualityMeasurementResponse(
                m.Id, m.CheckpointId, m.Checkpoint.Name, m.Checkpoint.Unit,
                m.WorkOrderUnitId, m.WorkOrderUnit != null ? m.WorkOrderUnit.SerialNumber : null,
                m.MeasuredValue, m.Checkpoint.LowerLimit, m.Checkpoint.UpperLimit,
                IsWithinTolerance(m.MeasuredValue, m.Checkpoint.LowerLimit, m.Checkpoint.UpperLimit),
                m.MeasuredAt, m.Notes))
            .ToListAsync(cancellationToken);

        return Ok(measurements);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<QualityMeasurementResponse>> CreateMeasurement(
        CreateQualityMeasurementRequest request,
        CancellationToken cancellationToken = default)
    {
        var checkpoint = await _dbContext.QualityCheckpoints.SingleOrDefaultAsync(c => c.Id == request.CheckpointId && c.IsActive, cancellationToken);
        if (checkpoint is null)
        {
            return BadRequest(new { message = "Checkpoint non trovato o non attivo." });
        }

        var workOrder = await _dbContext.WorkOrders.SingleOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return BadRequest(new { message = "Commessa non trovata." });
        }

        if (workOrder.ProductId != checkpoint.ProductId)
        {
            return BadRequest(new { message = "Questo checkpoint appartiene a un altro prodotto." });
        }

        if (request.WorkOrderUnitId.HasValue &&
            !await _dbContext.WorkOrderUnits.AnyAsync(u => u.Id == request.WorkOrderUnitId && u.WorkOrderId == request.WorkOrderId, cancellationToken))
        {
            return BadRequest(new { message = "Unità non trovata per questa commessa." });
        }

        if (request.MeasuredByUserId.HasValue &&
            !await _dbContext.Users.AnyAsync(u => u.Id == request.MeasuredByUserId, cancellationToken))
        {
            return BadRequest(new { message = "Utente non trovato." });
        }

        var measurement = new QualityMeasurement
        {
            CheckpointId = checkpoint.Id,
            WorkOrderId = workOrder.Id,
            WorkOrderUnitId = request.WorkOrderUnitId,
            MeasuredValue = request.MeasuredValue,
            MeasuredByUserId = request.MeasuredByUserId,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        _dbContext.QualityMeasurements.Add(measurement);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var unit = request.WorkOrderUnitId.HasValue
            ? await _dbContext.WorkOrderUnits.AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.WorkOrderUnitId, cancellationToken)
            : null;

        var response = new QualityMeasurementResponse(
            measurement.Id, checkpoint.Id, checkpoint.Name, checkpoint.Unit,
            measurement.WorkOrderUnitId, unit?.SerialNumber,
            measurement.MeasuredValue, checkpoint.LowerLimit, checkpoint.UpperLimit,
            IsWithinTolerance(measurement.MeasuredValue, checkpoint.LowerLimit, checkpoint.UpperLimit),
            measurement.MeasuredAt, measurement.Notes);
        return Created($"api/quality-measurements/{measurement.Id}", response);
    }

    /// <summary>Tutto ciò che serve al client per generare il PDF del certificato di conformità di una
    /// commessa: anagrafica commessa/prodotto + ogni misurazione registrata, già con pass/fail calcolato.
    /// La generazione del PDF vero e proprio resta lato client (QuestPDF), come l'etichetta QR.</summary>
    [HttpGet("certificate/{workOrderId:guid}")]
    public async Task<ActionResult<QualityCertificateResponse>> GetCertificateData(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var workOrder = await _dbContext.WorkOrders.AsNoTracking()
            .Include(w => w.Product)
            .SingleOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }

        var measurements = await _dbContext.QualityMeasurements.AsNoTracking()
            .Include(m => m.Checkpoint)
            .Include(m => m.WorkOrderUnit)
            .Where(m => m.WorkOrderId == workOrderId)
            .OrderBy(m => m.Checkpoint.Name).ThenBy(m => m.MeasuredAt)
            .Select(m => new QualityMeasurementResponse(
                m.Id, m.CheckpointId, m.Checkpoint.Name, m.Checkpoint.Unit,
                m.WorkOrderUnitId, m.WorkOrderUnit != null ? m.WorkOrderUnit.SerialNumber : null,
                m.MeasuredValue, m.Checkpoint.LowerLimit, m.Checkpoint.UpperLimit,
                IsWithinTolerance(m.MeasuredValue, m.Checkpoint.LowerLimit, m.Checkpoint.UpperLimit),
                m.MeasuredAt, m.Notes))
            .ToListAsync(cancellationToken);

        var allPassed = measurements.Count > 0 && measurements.All(m => m.IsWithinTolerance != false);

        return Ok(new QualityCertificateResponse(
            workOrder.Id, workOrder.Code, workOrder.ProductLotNumber, workOrder.Product.Code, workOrder.Product.Name,
            allPassed, measurements));
    }

    /// <summary>Null when the checkpoint has no limit on that side (a one-sided or purely observational
    /// measurement) — a null bound never fails the comparison.</summary>
    private static bool? IsWithinTolerance(decimal value, decimal? lowerLimit, decimal? upperLimit)
    {
        if (lowerLimit is null && upperLimit is null)
        {
            return null;
        }

        if (lowerLimit.HasValue && value < lowerLimit)
        {
            return false;
        }

        if (upperLimit.HasValue && value > upperLimit)
        {
            return false;
        }

        return true;
    }
}

public sealed record CreateQualityMeasurementRequest(
    Guid CheckpointId, Guid WorkOrderId, Guid? WorkOrderUnitId, decimal MeasuredValue, Guid? MeasuredByUserId, string? Notes);

public sealed record QualityMeasurementResponse(
    Guid Id, Guid CheckpointId, string CheckpointName, string? Unit,
    Guid? WorkOrderUnitId, string? UnitSerialNumber,
    decimal MeasuredValue, decimal? LowerLimit, decimal? UpperLimit, bool? IsWithinTolerance,
    DateTime MeasuredAt, string? Notes);

public sealed record QualityCertificateResponse(
    Guid WorkOrderId, string WorkOrderCode, string ProductLotNumber, string ProductCode, string ProductName,
    bool AllPassed, List<QualityMeasurementResponse> Measurements);
