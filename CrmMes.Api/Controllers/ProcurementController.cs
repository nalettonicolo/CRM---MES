using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/procurement")]
public class ProcurementController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public ProcurementController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("missing")]
    public async Task<ActionResult<IEnumerable<MissingMaterialResponse>>> GetMissingMaterials(
        [FromQuery] string status = "Open",
        CancellationToken cancellationToken = default)
    {
        var missing = await _dbContext.MissingMaterials
            .AsNoTracking()
            .Where(item => item.Status == status)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new MissingMaterialResponse(
                item.Id,
                item.WithdrawalSlipId,
                item.MaterialCode,
                item.Quantity,
                item.Source,
                item.Status,
                item.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(missing);
    }

    [Authorize(Policy = "PurchasingOrWarehouse")]
    [HttpGet("low-stock")]
    public async Task<ActionResult<IEnumerable<LowStockMaterialResponse>>> GetLowStockMaterials(
        CancellationToken cancellationToken = default)
    {
        var lowStock = await _dbContext.Materials
            .AsNoTracking()
            .Where(material => material.IsActive && material.MinStock > 0 && material.Stock < material.MinStock)
            .OrderBy(material => material.Stock - material.MinStock)
            .ToListAsync(cancellationToken);

        if (lowStock.Count == 0)
        {
            return Ok(Array.Empty<LowStockMaterialResponse>());
        }

        var codes = lowStock.Select(material => material.Code).ToArray();
        var alreadyRequested = await _dbContext.MissingMaterials
            .Where(mm => mm.Status == "Open" && mm.Source == "MinStock" && codes.Contains(mm.MaterialCode))
            .Select(mm => mm.MaterialCode)
            .Distinct()
            .ToListAsync(cancellationToken);
        var alreadyRequestedSet = alreadyRequested.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var response = lowStock.Select(material => new LowStockMaterialResponse(
            material.Id,
            material.Code,
            material.Name,
            material.Unit,
            material.Stock,
            material.MinStock,
            material.MinStock - material.Stock,
            alreadyRequestedSet.Contains(material.Code)));

        return Ok(response);
    }

    [Authorize(Policy = "PurchasingOrWarehouse")]
    [HttpPost("low-stock/scan")]
    public async Task<ActionResult<LowStockScanResponse>> ScanLowStock(
        CancellationToken cancellationToken = default)
    {
        var lowStock = await _dbContext.Materials
            .Where(material => material.IsActive && material.MinStock > 0 && material.Stock < material.MinStock)
            .ToListAsync(cancellationToken);

        var created = new List<MissingMaterial>();

        if (lowStock.Count > 0)
        {
            var codes = lowStock.Select(material => material.Code).ToArray();
            var alreadyOpen = await _dbContext.MissingMaterials
                .Where(mm => mm.Status == "Open" && mm.Source == "MinStock" && codes.Contains(mm.MaterialCode))
                .Select(mm => mm.MaterialCode)
                .ToListAsync(cancellationToken);
            var alreadyOpenSet = alreadyOpen.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var material in lowStock.Where(material => !alreadyOpenSet.Contains(material.Code)))
            {
                var missingMaterial = new MissingMaterial
                {
                    MaterialCode = material.Code,
                    Quantity = material.MinStock - material.Stock,
                    Source = "MinStock",
                    Status = "Open"
                };
                created.Add(missingMaterial);
                _dbContext.MissingMaterials.Add(missingMaterial);
            }

            if (created.Count > 0)
            {
                _dbContext.AuditLogs.Add(new AuditLog
                {
                    Action = "LowStockScan",
                    EntityType = "MissingMaterial",
                    UserName = GetCurrentUserName(),
                    Details = $"Scansione sottoscorte: creati {created.Count} materiali mancanti su {lowStock.Count} sotto minimo."
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return Ok(new LowStockScanResponse(
            lowStock.Count,
            created.Count,
            created.Select(mm => new MissingMaterialResponse(
                mm.Id,
                mm.WithdrawalSlipId,
                mm.MaterialCode,
                mm.Quantity,
                mm.Source,
                mm.Status,
                mm.CreatedAt)).ToList()));
    }

    [Authorize(Policy = "PurchasingOrWarehouse")]
    [HttpGet("purchase-orders")]
    public async Task<ActionResult<IEnumerable<PurchaseOrderSummaryResponse>>> GetPurchaseOrders(
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PurchaseOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(order => order.Status == status.Trim());
        }

        var orders = await query
            .OrderByDescending(order => order.CreatedAt)
            .Take(100)
            .Select(order => new PurchaseOrderSummaryResponse(
                order.Id,
                order.Code,
                order.SupplierId,
                order.Status,
                order.CreatedAt,
                order.ConfirmedAt,
                order.ReceivedAt,
                order.Items.Count))
            .ToListAsync(cancellationToken);

        return Ok(orders);
    }

    [Authorize(Policy = "PurchasingOrWarehouse")]
    [HttpGet("purchase-orders/{id:guid}")]
    public async Task<ActionResult<PurchaseOrderResponse>> GetPurchaseOrder(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.PurchaseOrders
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(order));
    }

    [Authorize(Policy = "Purchasing")]
    [HttpPost("purchase-orders")]
    public async Task<ActionResult<PurchaseOrderResponse>> CreatePurchaseOrder(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SupplierId == Guid.Empty || request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new { message = "Fornitore e almeno una riga sono obbligatori." });
        }

        if (!await _dbContext.Suppliers.AnyAsync(supplier => supplier.Id == request.SupplierId && supplier.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Fornitore non trovato o non attivo." });
        }

        var missingMaterialIds = request.Items
            .Where(item => item.MissingMaterialId.HasValue)
            .Select(item => item.MissingMaterialId!.Value)
            .Distinct()
            .ToArray();

        var missingMaterials = missingMaterialIds.Length == 0
            ? new Dictionary<Guid, MissingMaterial>()
            : await _dbContext.MissingMaterials
                .Where(mm => missingMaterialIds.Contains(mm.Id))
                .ToDictionaryAsync(mm => mm.Id, cancellationToken);

        foreach (var missingMaterialId in missingMaterialIds)
        {
            if (!missingMaterials.TryGetValue(missingMaterialId, out var missingMaterial) || missingMaterial.Status != "Open")
            {
                return BadRequest(new { message = $"Materiale mancante {missingMaterialId} non trovato o non aperto." });
            }
        }

        var order = new PurchaseOrder
        {
            Code = string.IsNullOrWhiteSpace(request.Code) ? $"OD-{DateTime.UtcNow:yyyyMMdd-HHmmss}" : request.Code.Trim(),
            SupplierId = request.SupplierId,
            Status = "Draft"
        };

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.MaterialCode) || item.Quantity <= 0)
            {
                return BadRequest(new { message = "Ogni riga deve avere codice e quantità positiva." });
            }

            order.Items.Add(new PurchaseOrderItem
            {
                MaterialCode = item.MaterialCode.Trim(),
                Description = item.Description?.Trim() ?? string.Empty,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                MissingMaterialId = item.MissingMaterialId
            });
        }

        foreach (var missingMaterial in missingMaterials.Values)
        {
            missingMaterial.Status = "Ordered";
        }

        _dbContext.PurchaseOrders.Add(order);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "PurchaseOrderCreated",
            EntityType = "PurchaseOrder",
            EntityId = order.Id,
            UserName = GetCurrentUserName(),
            Details = $"Ordine {order.Code} creato con {order.Items.Count} righe."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetPurchaseOrder), new { id = order.Id }, ToResponse(order));
    }

    [Authorize(Policy = "Purchasing")]
    [HttpPost("purchase-orders/{id:guid}/confirm")]
    public async Task<ActionResult<PurchaseOrderResponse>> ConfirmPurchaseOrder(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.PurchaseOrders
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != "Draft")
        {
            return Conflict(new { message = "Solo un ordine in bozza può essere confermato." });
        }

        order.Status = "Confirmed";
        order.ConfirmedAt = DateTime.UtcNow;

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "PurchaseOrderConfirmed",
            EntityType = "PurchaseOrder",
            EntityId = order.Id,
            UserName = GetCurrentUserName(),
            Details = $"Ordine {order.Code} confermato."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
    }

    [Authorize(Policy = "PurchasingOrWarehouse")]
    [HttpPost("purchase-orders/{id:guid}/receive")]
    public async Task<ActionResult<PurchaseOrderResponse>> ReceivePurchaseOrder(
        Guid id,
        ReceivePurchaseOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new { message = "Indicare almeno una riga ricevuta." });
        }

        if (request.Items.Any(item => item.Quantity <= 0))
        {
            return BadRequest(new { message = "Le quantità ricevute devono essere maggiori di zero." });
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await _dbContext.PurchaseOrders
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != "Confirmed" && order.Status != "PartiallyReceived")
        {
            return Conflict(new { message = "Solo un ordine confermato o parzialmente ricevuto può ricevere merce." });
        }

        var itemsById = order.Items.ToDictionary(item => item.Id);
        var overReceived = new List<string>();

        foreach (var requestItem in request.Items)
        {
            if (!itemsById.TryGetValue(requestItem.PurchaseOrderItemId, out var orderItem))
            {
                return BadRequest(new { message = $"Riga ordine {requestItem.PurchaseOrderItemId} non trovata." });
            }

            if (orderItem.ReceivedQuantity + requestItem.Quantity > orderItem.Quantity)
            {
                overReceived.Add(orderItem.MaterialCode);
            }
        }

        if (overReceived.Count > 0)
        {
            return Conflict(new { message = "Quantità ricevuta superiore all'ordinato per una o più righe.", materials = overReceived.Distinct().ToArray() });
        }

        var codes = request.Items
            .Select(requestItem => itemsById[requestItem.PurchaseOrderItemId].MaterialCode)
            .Distinct()
            .ToArray();
        var materials = await _dbContext.Materials
            .Where(material => codes.Contains(material.Code) && material.IsActive)
            .ToDictionaryAsync(material => material.Code, cancellationToken);

        var missingMaterialIds = request.Items
            .Select(requestItem => itemsById[requestItem.PurchaseOrderItemId].MissingMaterialId)
            .Where(missingMaterialId => missingMaterialId.HasValue)
            .Select(missingMaterialId => missingMaterialId!.Value)
            .Distinct()
            .ToArray();
        var missingMaterials = missingMaterialIds.Length == 0
            ? new Dictionary<Guid, MissingMaterial>()
            : await _dbContext.MissingMaterials
                .Where(mm => missingMaterialIds.Contains(mm.Id))
                .ToDictionaryAsync(mm => mm.Id, cancellationToken);

        var receivedDetails = new List<string>();

        foreach (var requestItem in request.Items)
        {
            var orderItem = itemsById[requestItem.PurchaseOrderItemId];
            orderItem.ReceivedQuantity += requestItem.Quantity;
            receivedDetails.Add($"{orderItem.MaterialCode}: +{requestItem.Quantity}");

            if (materials.TryGetValue(orderItem.MaterialCode, out var material))
            {
                material.Stock += requestItem.Quantity;
            }

            if (orderItem.MissingMaterialId.HasValue &&
                orderItem.ReceivedQuantity >= orderItem.Quantity &&
                missingMaterials.TryGetValue(orderItem.MissingMaterialId.Value, out var missingMaterial) &&
                missingMaterial.Status == "Ordered")
            {
                missingMaterial.Status = "Resolved";
            }
        }

        var fullyReceived = order.Items.All(item => item.ReceivedQuantity >= item.Quantity);
        order.Status = fullyReceived ? "Received" : "PartiallyReceived";
        if (fullyReceived)
        {
            order.ReceivedAt = DateTime.UtcNow;
        }

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "PurchaseOrderReceived",
            EntityType = "PurchaseOrder",
            EntityId = order.Id,
            UserName = GetCurrentUserName(),
            Details = $"Ordine {order.Code}: {string.Join(", ", receivedDetails)}."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(ToResponse(order));
    }

    [Authorize(Policy = "Purchasing")]
    [HttpPost("purchase-orders/{id:guid}/cancel")]
    public async Task<IActionResult> CancelPurchaseOrder(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.PurchaseOrders
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is "Received" or "Cancelled")
        {
            return Conflict(new { message = "Un ordine ricevuto o già annullato non può essere annullato." });
        }

        var missingMaterialIds = order.Items
            .Where(item => item.MissingMaterialId.HasValue)
            .Select(item => item.MissingMaterialId!.Value)
            .Distinct()
            .ToArray();

        if (missingMaterialIds.Length > 0)
        {
            var missingMaterials = await _dbContext.MissingMaterials
                .Where(mm => missingMaterialIds.Contains(mm.Id) && mm.Status == "Ordered")
                .ToListAsync(cancellationToken);

            foreach (var missingMaterial in missingMaterials)
            {
                missingMaterial.Status = "Open";
            }
        }

        order.Status = "Cancelled";

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "PurchaseOrderCancelled",
            EntityType = "PurchaseOrder",
            EntityId = order.Id,
            UserName = GetCurrentUserName(),
            Details = $"Ordine {order.Code} annullato."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private string? GetCurrentUserName() => User.FindFirstValue(ClaimTypes.Name);

    private static PurchaseOrderResponse ToResponse(PurchaseOrder order)
    {
        return new PurchaseOrderResponse(
            order.Id,
            order.Code,
            order.SupplierId,
            order.Status,
            order.CreatedAt,
            order.ConfirmedAt,
            order.ReceivedAt,
            order.Items.Select(item => new PurchaseOrderItemResponse(
                item.Id,
                item.MaterialCode,
                item.Description,
                item.Quantity,
                item.UnitPrice,
                item.ReceivedQuantity,
                item.MissingMaterialId)).ToList());
    }
}

public sealed record MissingMaterialResponse(
    Guid Id,
    Guid? WithdrawalSlipId,
    string MaterialCode,
    decimal Quantity,
    string Source,
    string Status,
    DateTime CreatedAt);

public sealed record LowStockMaterialResponse(
    Guid Id,
    string Code,
    string Name,
    string Unit,
    decimal Stock,
    decimal MinStock,
    decimal SuggestedQuantity,
    bool AlreadyRequested);

public sealed record LowStockScanResponse(
    int MaterialsBelowMinimum,
    int MissingMaterialsCreated,
    IReadOnlyList<MissingMaterialResponse> Created);

public sealed record CreatePurchaseOrderRequest(
    Guid SupplierId,
    string? Code,
    List<CreatePurchaseOrderItemRequest> Items);

public sealed record CreatePurchaseOrderItemRequest(
    string MaterialCode,
    decimal Quantity,
    string? Description,
    decimal UnitPrice,
    Guid? MissingMaterialId);

public sealed record ReceivePurchaseOrderRequest(
    List<ReceivePurchaseOrderItemRequest> Items);

public sealed record ReceivePurchaseOrderItemRequest(
    Guid PurchaseOrderItemId,
    decimal Quantity);

public sealed record PurchaseOrderResponse(
    Guid Id,
    string Code,
    Guid SupplierId,
    string Status,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? ReceivedAt,
    IReadOnlyList<PurchaseOrderItemResponse> Items);

public sealed record PurchaseOrderSummaryResponse(
    Guid Id,
    string Code,
    Guid SupplierId,
    string Status,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? ReceivedAt,
    int ItemCount);

public sealed record PurchaseOrderItemResponse(
    Guid Id,
    string MaterialCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal ReceivedQuantity,
    Guid? MissingMaterialId);
