using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Material lot traceability: batches of a material as they entered stock (purchase order
/// receipt, opening stock, or a manual correction), and the genealogy of what each lot was consumed
/// for. Complements <see cref="WorkOrdersController"/>'s ProductLotNumber, which is the finished-goods
/// side of the same traceability story.</summary>
[ApiController]
[Authorize]
[Route("api/material-lots")]
public class MaterialLotsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public MaterialLotsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MaterialLotSummaryResponse>>> GetMaterialLots(
        [FromQuery] string? materialCode = null,
        [FromQuery] bool onlyWithStock = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.MaterialLots.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(materialCode))
        {
            var code = materialCode.Trim();
            query = query.Where(lot => lot.MaterialCode == code);
        }

        if (onlyWithStock)
        {
            query = query.Where(lot => lot.Quantity > 0);
        }

        var lots = await query
            .OrderByDescending(lot => lot.ReceivedAt)
            .Take(200)
            .Select(lot => new MaterialLotSummaryResponse(
                lot.Id, lot.MaterialCode, lot.LotNumber, lot.Quantity, lot.InitialQuantity,
                lot.SupplierId, lot.PurchaseOrderId, lot.ReceivedAt))
            .ToListAsync(cancellationToken);

        return Ok(lots);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MaterialLotDetailResponse>> GetMaterialLot(
        Guid id, CancellationToken cancellationToken = default)
    {
        var lot = await _dbContext.MaterialLots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lot is null)
        {
            return NotFound();
        }

        // Forward genealogy: every withdrawal (and, through it, every work order) that consumed from
        // this lot — "what did this batch of material end up in?".
        var usages = await (
            from consumption in _dbContext.MaterialLotConsumptions.AsNoTracking()
            where consumption.MaterialLotId == id
            join item in _dbContext.WithdrawalItems.AsNoTracking() on consumption.WithdrawalItemId equals item.Id
            join slip in _dbContext.WithdrawalSlips.AsNoTracking() on item.WithdrawalSlipId equals slip.Id
            orderby consumption.CreatedAt
            select new MaterialLotUsageResponse(
                consumption.Id, consumption.Quantity, consumption.CreatedAt,
                slip.Id, slip.Code, slip.WorkOrderId))
            .ToListAsync(cancellationToken);

        return Ok(new MaterialLotDetailResponse(
            lot.Id, lot.MaterialCode, lot.LotNumber, lot.Quantity, lot.InitialQuantity,
            lot.SupplierId, lot.PurchaseOrderId, lot.ReceivedAt, lot.Notes, usages));
    }

    /// <summary>Manual lot intake: for stock that enters outside the purchase-order flow (opening
    /// balance corrections, free samples, internal sub-assemblies). Increases Material.Stock the same
    /// way receiving a purchase order does, so the two paths stay consistent.</summary>
    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<MaterialLotSummaryResponse>> CreateMaterialLot(
        CreateMaterialLotRequest request, CancellationToken cancellationToken = default)
    {
        var materialCode = request.MaterialCode?.Trim();
        var lotNumber = request.LotNumber?.Trim();

        if (string.IsNullOrWhiteSpace(materialCode) || string.IsNullOrWhiteSpace(lotNumber) || request.Quantity <= 0)
        {
            return BadRequest(new { message = "Codice materiale, numero lotto e quantità (maggiore di zero) sono obbligatori." });
        }

        var material = await _dbContext.Materials.SingleOrDefaultAsync(m => m.Code == materialCode && m.IsActive, cancellationToken);
        if (material is null)
        {
            return BadRequest(new { message = "Materiale non trovato o non attivo." });
        }

        if (await _dbContext.MaterialLots.AnyAsync(l => l.MaterialCode == materialCode && l.LotNumber == lotNumber, cancellationToken))
        {
            return Conflict(new { message = "Esiste già un lotto con questo numero per questo materiale." });
        }

        var lot = new MaterialLot
        {
            MaterialCode = materialCode,
            LotNumber = lotNumber,
            Quantity = request.Quantity,
            InitialQuantity = request.Quantity,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        material.Stock += request.Quantity;

        _dbContext.MaterialLots.Add(lot);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "MaterialLotCreated",
            EntityType = "MaterialLot",
            EntityId = lot.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Lotto {lot.LotNumber} creato manualmente per {lot.MaterialCode}, quantità {lot.Quantity}."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetMaterialLot),
            new { id = lot.Id },
            new MaterialLotSummaryResponse(lot.Id, lot.MaterialCode, lot.LotNumber, lot.Quantity, lot.InitialQuantity, lot.SupplierId, lot.PurchaseOrderId, lot.ReceivedAt));
    }
}

public sealed record CreateMaterialLotRequest(string? MaterialCode, string? LotNumber, decimal Quantity, string? Notes);

public sealed record MaterialLotSummaryResponse(
    Guid Id,
    string MaterialCode,
    string LotNumber,
    decimal Quantity,
    decimal InitialQuantity,
    Guid? SupplierId,
    Guid? PurchaseOrderId,
    DateTime ReceivedAt);

public sealed record MaterialLotDetailResponse(
    Guid Id,
    string MaterialCode,
    string LotNumber,
    decimal Quantity,
    decimal InitialQuantity,
    Guid? SupplierId,
    Guid? PurchaseOrderId,
    DateTime ReceivedAt,
    string? Notes,
    IReadOnlyList<MaterialLotUsageResponse> Usages);

public sealed record MaterialLotUsageResponse(
    Guid ConsumptionId,
    decimal Quantity,
    DateTime ConsumedAt,
    Guid WithdrawalSlipId,
    string WithdrawalSlipCode,
    Guid? WorkOrderId);
