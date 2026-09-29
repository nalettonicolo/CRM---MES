using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Costo e margine di commessa, prezzo di vendita e ore lavorate registrate a mano.</summary>
[ApiController]
[Authorize]
[Route("api/work-orders/{workOrderId:guid}")]
public class WorkOrderCostingController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly WorkOrderCosting _costing;

    public WorkOrderCostingController(ApplicationDbContext dbContext, WorkOrderCosting costing)
    {
        _dbContext = dbContext;
        _costing = costing;
    }

    [Authorize(Policy = "ViewMargins")]
    [HttpGet("costing")]
    public async Task<ActionResult<WorkOrderCostingResult>> GetCosting(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var result = await _costing.CalculateAsync(workOrderId, DateTime.UtcNow, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Sets (or clears, with null) the work order's revenue. Conversion from a quote already
    /// sets it; this covers work orders created by hand and later price agreements.</summary>
    [Authorize(Policy = "SalesOrWarehouse")]
    [HttpPut("sale-price")]
    public async Task<IActionResult> SetSalePrice(Guid workOrderId, SetSalePriceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SalePrice is < 0)
        {
            return BadRequest(new { message = "Il prezzo di vendita non può essere negativo." });
        }

        var order = await _dbContext.WorkOrders.SingleOrDefaultAsync(o => o.Id == workOrderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        order.SalePrice = request.SalePrice;
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WorkOrderSalePriceSet",
            EntityType = "WorkOrder",
            EntityId = order.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = request.SalePrice is { } price
                ? $"Prezzo di vendita della commessa {order.Code} impostato a {price:0.00} €."
                : $"Prezzo di vendita della commessa {order.Code} rimosso."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("labor")]
    public async Task<ActionResult<IEnumerable<LaborEntryResponse>>> GetLaborEntries(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var entries = await _dbContext.LaborEntries.AsNoTracking()
            .Where(e => e.WorkOrderId == workOrderId)
            .OrderBy(e => e.WorkDate)
            .Select(e => new LaborEntryResponse(
                e.Id, e.WorkDate, e.Minutes, e.OperatorName, e.UserId, e.WorkCenterId,
                e.WorkCenter != null ? e.WorkCenter.Name : null, e.WorkOrderOperationId, e.Notes))
            .ToListAsync(cancellationToken);
        return Ok(entries);
    }

    /// <summary>Registers hours worked on the job outside a phase start/complete (bench wiring outside the
    /// routing, on-site installation, rework...). Any authenticated user may log hours.</summary>
    [HttpPost("labor")]
    public async Task<ActionResult<LaborEntryResponse>> AddLaborEntry(
        Guid workOrderId, AddLaborEntryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Minutes <= 0 || request.Minutes > 24 * 60)
        {
            return BadRequest(new { message = "Indica una durata tra 1 minuto e 24 ore." });
        }

        var order = await _dbContext.WorkOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == workOrderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status == "Cancelled")
        {
            return Conflict(new { message = "Non si registrano ore su una commessa annullata." });
        }

        if (request.OperationId is { } operationId &&
            !await _dbContext.WorkOrderOperations.AnyAsync(op => op.Id == operationId && op.WorkOrderId == workOrderId, cancellationToken))
        {
            return BadRequest(new { message = "La fase indicata non appartiene a questa commessa." });
        }

        WorkCenter? workCenter = null;
        if (request.WorkCenterId is { } workCenterId)
        {
            workCenter = await _dbContext.WorkCenters.AsNoTracking().SingleOrDefaultAsync(w => w.Id == workCenterId, cancellationToken);
            if (workCenter is null)
            {
                return BadRequest(new { message = "Centro di lavoro non trovato." });
            }
        }

        string? operatorName = null;
        if (request.UserId is { } userId)
        {
            operatorName = await _dbContext.Users.Where(u => u.Id == userId).Select(u => u.Name).SingleOrDefaultAsync(cancellationToken);
            if (operatorName is null)
            {
                return BadRequest(new { message = "Operatore non trovato." });
            }
        }

        var entry = new LaborEntry
        {
            WorkOrderId = workOrderId,
            WorkOrderOperationId = request.OperationId,
            WorkCenterId = request.WorkCenterId,
            UserId = request.UserId,
            OperatorName = operatorName ?? User.FindFirstValue(ClaimTypes.Name),
            Minutes = request.Minutes,
            WorkDate = (request.WorkDate ?? DateTime.UtcNow).Date,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedBy = User.FindFirstValue(ClaimTypes.Name)
        };
        _dbContext.LaborEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new LaborEntryResponse(
            entry.Id, entry.WorkDate, entry.Minutes, entry.OperatorName, entry.UserId, entry.WorkCenterId,
            workCenter?.Name, entry.WorkOrderOperationId, entry.Notes));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpDelete("labor/{entryId:guid}")]
    public async Task<IActionResult> DeleteLaborEntry(Guid workOrderId, Guid entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.LaborEntries.SingleOrDefaultAsync(e => e.Id == entryId && e.WorkOrderId == workOrderId, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        _dbContext.LaborEntries.Remove(entry);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "LaborEntryDeleted",
            EntityType = "WorkOrder",
            EntityId = workOrderId,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Eliminata registrazione di {entry.Minutes:0.#} minuti del {entry.WorkDate:dd/MM/yyyy} ({entry.OperatorName})."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record SetSalePriceRequest(decimal? SalePrice);

public sealed record AddLaborEntryRequest(
    decimal Minutes, DateTime? WorkDate, Guid? WorkCenterId, Guid? OperationId, Guid? UserId, string? Notes);

public sealed record LaborEntryResponse(
    Guid Id, DateTime WorkDate, decimal Minutes, string? OperatorName, Guid? UserId,
    Guid? WorkCenterId, string? WorkCenterName, Guid? OperationId, string? Notes);
