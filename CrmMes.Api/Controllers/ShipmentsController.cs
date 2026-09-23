using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Shipments moved by a carrier, in either direction: Inbound (goods arriving, optionally
/// fulfilling a purchase order) or Outbound (goods leaving, optionally delivering a work order). A thin,
/// parallel tracking layer — creating or advancing a shipment never changes the linked order/job's own
/// status, and neither link is required, so a shipment can stand alone.</summary>
[ApiController]
[Authorize]
[Route("api/shipments")]
public class ShipmentsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public ShipmentsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShipmentSummaryResponse>>> GetShipments(
        [FromQuery] string? direction = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Shipments.AsNoTracking().Include(s => s.Carrier).AsQueryable();
        if (!string.IsNullOrWhiteSpace(direction))
        {
            query = query.Where(s => s.Direction == direction.Trim());
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(s => s.Status == status.Trim());
        }

        var shipments = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(200)
            .Select(s => new ShipmentSummaryResponse(
                s.Id, s.Code, s.Direction, s.Status, s.Carrier.Name, s.TrackingNumber,
                s.CounterpartReference, s.ExpectedAt, s.ShippedAt, s.DeliveredAt, s.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(shipments);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ShipmentResponse>> GetShipment(Guid id, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments
            .AsNoTracking()
            .Include(s => s.Carrier)
            .Include(s => s.PurchaseOrder)
            .Include(s => s.WorkOrder)
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

        return shipment is null ? NotFound() : Ok(ToResponse(shipment));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<ShipmentResponse>> CreateShipment(
        CreateShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var direction = request.Direction?.Trim();
        if (direction is not ("Inbound" or "Outbound"))
        {
            return BadRequest(new { message = "La direzione deve essere \"Inbound\" o \"Outbound\"." });
        }

        if (request.PurchaseOrderId.HasValue && direction != "Inbound")
        {
            return BadRequest(new { message = "Un ordine fornitore può essere collegato solo a una spedizione in ingresso." });
        }

        if (request.WorkOrderId.HasValue && direction != "Outbound")
        {
            return BadRequest(new { message = "Una commessa può essere collegata solo a una spedizione in uscita." });
        }

        var carrier = await _dbContext.Carriers
            .SingleOrDefaultAsync(c => c.Id == request.CarrierId && c.IsActive, cancellationToken);
        if (carrier is null)
        {
            return BadRequest(new { message = "Corriere non trovato o non attivo." });
        }

        if (request.PurchaseOrderId.HasValue &&
            !await _dbContext.PurchaseOrders.AnyAsync(po => po.Id == request.PurchaseOrderId, cancellationToken))
        {
            return BadRequest(new { message = "Ordine fornitore non trovato." });
        }

        if (request.WorkOrderId.HasValue &&
            !await _dbContext.WorkOrders.AnyAsync(wo => wo.Id == request.WorkOrderId, cancellationToken))
        {
            return BadRequest(new { message = "Commessa non trovata." });
        }

        var shipment = new Shipment
        {
            Code = $"SPD-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
            Direction = direction,
            CarrierId = carrier.Id,
            TrackingNumber = string.IsNullOrWhiteSpace(request.TrackingNumber) ? null : request.TrackingNumber.Trim(),
            PurchaseOrderId = request.PurchaseOrderId,
            WorkOrderId = request.WorkOrderId,
            CounterpartReference = string.IsNullOrWhiteSpace(request.CounterpartReference) ? null : request.CounterpartReference.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ExpectedAt = request.ExpectedAt,
            Status = "Preparing"
        };

        _dbContext.Shipments.Add(shipment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        shipment.Carrier = carrier;
        return CreatedAtAction(nameof(GetShipment), new { id = shipment.Id }, ToResponse(shipment));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/ship")]
    public async Task<ActionResult<ShipmentResponse>> ShipShipment(
        Guid id, ShipShipmentRequest? request, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments
            .Include(s => s.Carrier).Include(s => s.PurchaseOrder).Include(s => s.WorkOrder)
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shipment is null)
        {
            return NotFound();
        }

        if (shipment.Status != "Preparing")
        {
            return Conflict(new { message = "Solo una spedizione in preparazione può essere avviata." });
        }

        if (!string.IsNullOrWhiteSpace(request?.TrackingNumber))
        {
            shipment.TrackingNumber = request.TrackingNumber.Trim();
        }

        shipment.Status = "Shipped";
        shipment.ShippedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(shipment));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/deliver")]
    public async Task<ActionResult<ShipmentResponse>> DeliverShipment(Guid id, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments
            .Include(s => s.Carrier).Include(s => s.PurchaseOrder).Include(s => s.WorkOrder)
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shipment is null)
        {
            return NotFound();
        }

        if (shipment.Status != "Shipped")
        {
            return Conflict(new { message = "Solo una spedizione partita può essere segnata come consegnata." });
        }

        shipment.Status = "Delivered";
        shipment.DeliveredAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(shipment));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ShipmentResponse>> CancelShipment(Guid id, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments
            .Include(s => s.Carrier).Include(s => s.PurchaseOrder).Include(s => s.WorkOrder)
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shipment is null)
        {
            return NotFound();
        }

        if (shipment.Status is not ("Preparing" or "Shipped"))
        {
            return Conflict(new { message = "Una spedizione consegnata o già annullata non può essere annullata." });
        }

        shipment.Status = "Cancelled";
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(shipment));
    }

    private static ShipmentResponse ToResponse(Shipment shipment) => new(
        shipment.Id, shipment.Code, shipment.Direction, shipment.Status,
        shipment.CarrierId, shipment.Carrier.Name, shipment.TrackingNumber,
        shipment.PurchaseOrderId, shipment.PurchaseOrder?.Code,
        shipment.WorkOrderId, shipment.WorkOrder?.Code,
        shipment.CounterpartReference, shipment.Address, shipment.Notes,
        shipment.ExpectedAt, shipment.ShippedAt, shipment.DeliveredAt, shipment.CreatedAt);
}

public sealed record CreateShipmentRequest(
    string? Direction,
    Guid CarrierId,
    string? TrackingNumber,
    Guid? PurchaseOrderId,
    Guid? WorkOrderId,
    string? CounterpartReference,
    string? Address,
    string? Notes,
    DateTime? ExpectedAt);

public sealed record ShipShipmentRequest(string? TrackingNumber);

public sealed record ShipmentSummaryResponse(
    Guid Id,
    string Code,
    string Direction,
    string Status,
    string CarrierName,
    string? TrackingNumber,
    string? CounterpartReference,
    DateTime? ExpectedAt,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime CreatedAt);

public sealed record ShipmentResponse(
    Guid Id,
    string Code,
    string Direction,
    string Status,
    Guid CarrierId,
    string CarrierName,
    string? TrackingNumber,
    Guid? PurchaseOrderId,
    string? PurchaseOrderCode,
    Guid? WorkOrderId,
    string? WorkOrderCode,
    string? CounterpartReference,
    string? Address,
    string? Notes,
    DateTime? ExpectedAt,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime CreatedAt);
