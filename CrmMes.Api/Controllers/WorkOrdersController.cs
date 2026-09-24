using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Production jobs (commesse): build N units of a product, tracked through the routing
/// operations snapshotted from the product at creation time, with a one-click pick list generated
/// from its bill of materials.</summary>
[ApiController]
[Authorize]
[Route("api/work-orders")]
public class WorkOrdersController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly WithdrawalItemBuilder _itemBuilder;

    public WorkOrdersController(ApplicationDbContext dbContext, WithdrawalItemBuilder itemBuilder)
    {
        _dbContext = dbContext;
        _itemBuilder = itemBuilder;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkOrderSummaryResponse>>> GetWorkOrders(
        [FromQuery] string? status = null,
        [FromQuery] Guid? siteId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.WorkOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(order => order.Status == status.Trim());
        }

        if (siteId.HasValue)
        {
            query = query.Where(order => order.Area != null && order.Area.SiteId == siteId);
        }

        var orders = await query
            .OrderByDescending(order => order.CreatedAt)
            .Take(100)
            .Select(order => new WorkOrderSummaryResponse(
                order.Id,
                order.Code,
                order.ProductLotNumber,
                order.ProductId,
                order.Product.Code,
                order.Product.Name,
                order.Quantity,
                order.Status,
                order.DueDate,
                order.CreatedAt,
                order.Operations.Count,
                order.Operations.Count(op => op.Status == "Done")))
            .ToListAsync(cancellationToken);

        return Ok(orders);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkOrderResponse>> GetWorkOrder(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders
            .AsNoTracking()
            .Include(o => o.Operations)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order is null ? NotFound() : Ok(ToResponse(order));
    }

    /// <summary>Looks a work order up by its human-readable code instead of its id — what a shop-floor
    /// terminal needs after reading a barcode/QR label printed on the job traveler, since the operator
    /// scans a printed code, not a GUID.</summary>
    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<WorkOrderResponse>> GetWorkOrderByCode(string code, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders
            .AsNoTracking()
            .Include(o => o.Operations)
            .SingleOrDefaultAsync(o => o.Code == code, cancellationToken);

        return order is null ? NotFound() : Ok(ToResponse(order));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<WorkOrderResponse>> CreateWorkOrder(
        CreateWorkOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProductId == Guid.Empty || request.Quantity <= 0)
        {
            return BadRequest(new { message = "Prodotto e quantità (maggiore di zero) sono obbligatori." });
        }

        var product = await _dbContext.Products
            .Include(p => p.RoutingSteps)
            .SingleOrDefaultAsync(p => p.Id == request.ProductId && p.IsActive, cancellationToken);

        if (product is null)
        {
            return BadRequest(new { message = "Prodotto non trovato o non attivo." });
        }

        if (request.AreaId.HasValue &&
            !await _dbContext.Areas.AnyAsync(area => area.Id == request.AreaId && area.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Area non trovata o non attiva." });
        }

        var order = new WorkOrder
        {
            // Random suffix avoids collisions when two work orders are created within the same second.
            Code = string.IsNullOrWhiteSpace(request.Code)
                ? $"WO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}"
                : request.Code.Trim(),
            ProductLotNumber = string.IsNullOrWhiteSpace(request.ProductLotNumber)
                ? $"LOT-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}"
                : request.ProductLotNumber.Trim(),
            ProductId = product.Id,
            Quantity = request.Quantity,
            AreaId = request.AreaId,
            CustomerReference = string.IsNullOrWhiteSpace(request.CustomerReference) ? null : request.CustomerReference.Trim(),
            DueDate = request.DueDate,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Status = "Draft"
        };

        // Snapshot the product's routing now: later edits to the product's template must not
        // retroactively change a job that may already be on the floor.
        foreach (var step in product.RoutingSteps.OrderBy(s => s.SequenceNumber))
        {
            order.Operations.Add(new WorkOrderOperation
            {
                WorkOrderId = order.Id,
                SequenceNumber = step.SequenceNumber,
                Name = step.Name,
                Description = step.Description,
                WorkCenter = step.WorkCenter,
                EstimatedMinutes = step.EstimatedMinutes,
                Status = "Pending"
            });
        }

        // Per-serial tracking only makes sense for a whole number of discrete units — a continuous or
        // bulk quantity (e.g. 2.5 kg) has nothing to number, so no units are generated for it and it
        // keeps using the batch-level lot number and quality approximation instead.
        if (order.Quantity == Math.Floor(order.Quantity) && order.Quantity > 0)
        {
            for (var sequence = 1; sequence <= (int)order.Quantity; sequence++)
            {
                var unit = new WorkOrderUnit
                {
                    WorkOrderId = order.Id,
                    SequenceNumber = sequence,
                    SerialNumber = $"{order.Code}-{sequence:000}",
                    Status = "Pending"
                };

                // Per-unit phase history starts as a full Pending grid — one row per operation — so
                // querying "what has unit N gone through" always has an answer, even before the first
                // phase starts. StartOperation/CompleteOperation below project the batch-level timing
                // onto these rows for every unit that's still Pending.
                foreach (var operation in order.Operations)
                {
                    unit.Operations.Add(new WorkOrderUnitOperation
                    {
                        WorkOrderUnitId = unit.Id,
                        WorkOrderOperationId = operation.Id,
                        Status = "Pending"
                    });
                }

                order.Units.Add(unit);
            }
        }

        _dbContext.WorkOrders.Add(order);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WorkOrderCreated",
            EntityType = "WorkOrder",
            EntityId = order.Id,
            UserName = GetCurrentUserName(),
            Details = $"Commessa {order.Code} creata per {order.Quantity} x {product.Code}, {order.Operations.Count} fasi."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetWorkOrder), new { id = order.Id }, ToResponse(order));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkOrderResponse>> EditWorkOrder(
        Guid id,
        EditWorkOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { message = "La quantità deve essere maggiore di zero." });
        }

        var order = await _dbContext.WorkOrders
            .Include(o => o.Operations)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != "Draft")
        {
            return Conflict(new { message = "Solo una commessa in bozza può essere modificata." });
        }

        if (request.AreaId.HasValue &&
            !await _dbContext.Areas.AnyAsync(area => area.Id == request.AreaId && area.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Area non trovata o non attiva." });
        }

        order.Quantity = request.Quantity;
        order.AreaId = request.AreaId;
        order.CustomerReference = string.IsNullOrWhiteSpace(request.CustomerReference) ? null : request.CustomerReference.Trim();
        order.DueDate = request.DueDate;
        order.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
    }

    /// <summary>Compares the product's bill of materials, scaled by this work order's quantity,
    /// against current material stock. Read-only: lets the client warn the operator before release
    /// without committing to anything (the "material readiness" check a false-availability start
    /// would otherwise skip).</summary>
    [HttpGet("{id:guid}/material-check")]
    public async Task<ActionResult<MaterialAvailabilityResponse>> CheckMaterialAvailability(
        Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        return Ok(await BuildAvailabilityResponseAsync(order, cancellationToken));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/release")]
    public async Task<ActionResult<WorkOrderResponse>> ReleaseWorkOrder(
        Guid id, [FromQuery] bool force = false, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders
            .Include(o => o.Operations)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != "Draft")
        {
            return Conflict(new { message = "Solo una commessa in bozza può essere rilasciata." });
        }

        var availability = await BuildAvailabilityResponseAsync(order, cancellationToken);
        if (!availability.IsAvailable && !force)
        {
            return Conflict(new { message = "Materiali insufficienti per coprire questa commessa.", availability });
        }

        order.Status = "Released";
        order.ReleasedAt = DateTime.UtcNow;
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WorkOrderReleased",
            EntityType = "WorkOrder",
            EntityId = order.Id,
            UserName = GetCurrentUserName(),
            Details = availability.IsAvailable
                ? $"Commessa {order.Code} rilasciata in produzione."
                : $"Commessa {order.Code} rilasciata in produzione nonostante materiali insufficienti (forzato dall'operatore)."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
    }

    private async Task<MaterialAvailabilityResponse> BuildAvailabilityResponseAsync(WorkOrder order, CancellationToken cancellationToken)
    {
        var bomItems = await _dbContext.BillOfMaterialItems
            .AsNoTracking()
            .Where(item => item.ProductId == order.ProductId)
            .ToListAsync(cancellationToken);

        if (bomItems.Count == 0)
        {
            return new MaterialAvailabilityResponse(true, []);
        }

        var codes = bomItems.Select(item => item.MaterialCode).Distinct().ToArray();
        var stockByCode = await _dbContext.Materials
            .AsNoTracking()
            .Where(material => codes.Contains(material.Code))
            .ToDictionaryAsync(material => material.Code, material => material.Stock, cancellationToken);

        var lines = bomItems
            .Select(item =>
            {
                var required = item.Quantity * order.Quantity;
                var available = stockByCode.GetValueOrDefault(item.MaterialCode, 0);
                var shortfall = Math.Max(0, required - available);
                return new MaterialAvailabilityLineResponse(item.MaterialCode, required, available, shortfall);
            })
            .ToList();

        return new MaterialAvailabilityResponse(lines.All(line => line.Shortfall == 0), lines);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelWorkOrder(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is "Completed" or "Cancelled")
        {
            return Conflict(new { message = "Una commessa completata o già annullata non può essere annullata." });
        }

        order.Status = "Cancelled";
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WorkOrderCancelled",
            EntityType = "WorkOrder",
            EntityId = order.Id,
            UserName = GetCurrentUserName(),
            Details = $"Commessa {order.Code} annullata."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<WorkOrderResponse>> CompleteWorkOrder(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders
            .Include(o => o.Operations)
            .Include(o => o.Units)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is not ("Released" or "InProgress"))
        {
            return Conflict(new { message = "Solo una commessa rilasciata o in corso può essere completata." });
        }

        if (order.Operations.Any(op => op.Status != "Done"))
        {
            return Conflict(new { message = "Tutte le fasi devono essere completate prima di chiudere la commessa." });
        }

        // Any unit nothing scrapped along the way ships as-is: it was never flagged, so it's good.
        var completedAt = DateTime.UtcNow;
        foreach (var unit in order.Units.Where(u => u.Status == "Pending"))
        {
            unit.Status = "Good";
            unit.ResolvedAt = completedAt;
        }

        order.Status = "Completed";
        order.CompletedAt = completedAt;
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WorkOrderCompleted",
            EntityType = "WorkOrder",
            EntityId = order.Id,
            UserName = GetCurrentUserName(),
            Details = $"Commessa {order.Code} completata."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
    }

    [Authorize]
    [HttpPost("{id:guid}/operations/{operationId:guid}/start")]
    public async Task<ActionResult<WorkOrderResponse>> StartOperation(
        Guid id, Guid operationId, [FromQuery] string? operatorName = null, [FromQuery] Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders
            .Include(o => o.Operations)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is not ("Released" or "InProgress"))
        {
            return Conflict(new { message = "Le fasi si possono avviare solo su una commessa rilasciata o in corso." });
        }

        var operation = order.Operations.SingleOrDefault(op => op.Id == operationId);
        if (operation is null)
        {
            return NotFound();
        }

        if (operation.Status != "Pending")
        {
            return Conflict(new { message = "Solo una fase in attesa può essere avviata." });
        }

        operation.Status = "InProgress";
        operation.StartedAt = DateTime.UtcNow;
        operation.StartedBy = string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim();
        operation.StartedByUserId = operatorId;
        if (order.Status == "Released")
        {
            order.Status = "InProgress";
        }

        await ProjectOperationTimingToUnitsAsync(id, operationId, "InProgress", operation.StartedAt, null, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
    }

    /// <summary>Mirrors a batch-level operation's start/complete timing onto every unit that's still
    /// Pending (not yet scrapped) — see WorkOrderUnitOperation. A no-op when the order has no tracked
    /// units (non-integer quantity).</summary>
    private async Task ProjectOperationTimingToUnitsAsync(
        Guid workOrderId, Guid operationId, string status, DateTime? startedAt, DateTime? completedAt, CancellationToken cancellationToken)
    {
        var unitOperations = await _dbContext.WorkOrderUnitOperations
            .Where(uo => uo.WorkOrderOperationId == operationId && uo.Unit.WorkOrderId == workOrderId && uo.Unit.Status == "Pending")
            .ToListAsync(cancellationToken);

        foreach (var unitOperation in unitOperations)
        {
            unitOperation.Status = status;
            if (startedAt.HasValue)
            {
                unitOperation.StartedAt = startedAt;
            }

            if (completedAt.HasValue)
            {
                unitOperation.CompletedAt = completedAt;
            }
        }
    }

    [Authorize]
    [HttpPost("{id:guid}/operations/{operationId:guid}/complete")]
    public async Task<ActionResult<WorkOrderResponse>> CompleteOperation(
        Guid id, Guid operationId, [FromQuery] string? operatorName = null, [FromQuery] Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders
            .Include(o => o.Operations)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        var operation = order.Operations.SingleOrDefault(op => op.Id == operationId);
        if (operation is null)
        {
            return NotFound();
        }

        if (operation.Status != "InProgress")
        {
            return Conflict(new { message = "Solo una fase avviata può essere completata." });
        }

        var hasOpenDowntime = await _dbContext.OperationDowntimes
            .AnyAsync(d => d.WorkOrderOperationId == operationId && d.EndedAt == null, cancellationToken);
        if (hasOpenDowntime)
        {
            return Conflict(new { message = "Questa fase ha un fermo macchina ancora aperto: chiudilo prima di completare la fase." });
        }

        operation.Status = "Done";
        operation.CompletedAt = DateTime.UtcNow;
        operation.CompletedBy = string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim();
        operation.CompletedByUserId = operatorId;

        await ProjectOperationTimingToUnitsAsync(id, operationId, "Done", null, operation.CompletedAt, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
    }

    /// <summary>Logs the start of a machine stop during an operation — the Availability component of
    /// OEE. Only one stop can be open at a time per operation; only a running (InProgress) operation can
    /// have one, since a stop is something that interrupts work already underway.</summary>
    [Authorize]
    [HttpPost("{id:guid}/operations/{operationId:guid}/downtime/start")]
    public async Task<ActionResult<OperationDowntimeResponse>> StartDowntime(
        Guid id, Guid operationId, StartDowntimeRequest request, [FromQuery] string? operatorName = null, [FromQuery] Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { message = "Indica il motivo del fermo." });
        }

        var operation = await _dbContext.WorkOrderOperations
            .SingleOrDefaultAsync(op => op.Id == operationId && op.WorkOrderId == id, cancellationToken);
        if (operation is null)
        {
            return NotFound();
        }

        if (operation.Status != "InProgress")
        {
            return Conflict(new { message = "Si può registrare un fermo solo su una fase avviata." });
        }

        var alreadyOpen = await _dbContext.OperationDowntimes
            .AnyAsync(d => d.WorkOrderOperationId == operationId && d.EndedAt == null, cancellationToken);
        if (alreadyOpen)
        {
            return Conflict(new { message = "C'è già un fermo aperto per questa fase." });
        }

        var downtime = new OperationDowntime
        {
            WorkOrderOperationId = operationId,
            Reason = request.Reason.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ReportedBy = string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim(),
            ReportedByUserId = operatorId
        };

        _dbContext.OperationDowntimes.Add(downtime);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDowntimeResponse(downtime));
    }

    [Authorize]
    [HttpPost("{id:guid}/operations/{operationId:guid}/downtime/{downtimeId:guid}/end")]
    public async Task<ActionResult<OperationDowntimeResponse>> EndDowntime(
        Guid id, Guid operationId, Guid downtimeId, [FromQuery] string? operatorName = null, [FromQuery] Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        var downtime = await _dbContext.OperationDowntimes
            .SingleOrDefaultAsync(d => d.Id == downtimeId && d.WorkOrderOperationId == operationId, cancellationToken);
        if (downtime is null)
        {
            return NotFound();
        }

        if (downtime.EndedAt is not null)
        {
            return Conflict(new { message = "Questo fermo è già stato chiuso." });
        }

        downtime.EndedAt = DateTime.UtcNow;
        downtime.ClosedBy = string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim();
        downtime.ClosedByUserId = operatorId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDowntimeResponse(downtime));
    }

    [HttpGet("{id:guid}/operations/{operationId:guid}/downtimes")]
    public async Task<ActionResult<IEnumerable<OperationDowntimeResponse>>> GetDowntimes(
        Guid id, Guid operationId, CancellationToken cancellationToken = default)
    {
        var downtimes = await _dbContext.OperationDowntimes
            .AsNoTracking()
            .Where(d => d.WorkOrderOperationId == operationId)
            .OrderByDescending(d => d.StartedAt)
            .ToListAsync(cancellationToken);

        return Ok(downtimes.Select(ToDowntimeResponse));
    }

    private static OperationDowntimeResponse ToDowntimeResponse(OperationDowntime downtime) => new(
        downtime.Id,
        downtime.Reason,
        downtime.Notes,
        downtime.StartedAt,
        downtime.EndedAt,
        downtime.EndedAt.HasValue ? (decimal)(downtime.EndedAt.Value - downtime.StartedAt).TotalMinutes : null,
        downtime.ReportedBy,
        downtime.ClosedBy);

    /// <summary>Logs a quality defect found during or after an operation — the Quality component of
    /// OEE. Unlike a downtime this isn't a start/end interval, just a point-in-time record of what was
    /// found and how many units it affected, so it can be registered on an operation that has already
    /// finished (a defect discovered at final inspection), not only while it's running.</summary>
    [Authorize]
    [HttpPost("{id:guid}/operations/{operationId:guid}/non-conformities")]
    public async Task<ActionResult<NonConformityResponse>> RegisterNonConformity(
        Guid id, Guid operationId, RegisterNonConformityRequest request, [FromQuery] string? operatorName = null, [FromQuery] Guid? operatorId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return BadRequest(new { message = "Indica la descrizione della non conformità." });
        }

        if (request.ScrapQuantity <= 0)
        {
            return BadRequest(new { message = "La quantità scartata deve essere maggiore di zero." });
        }

        var operation = await _dbContext.WorkOrderOperations
            .SingleOrDefaultAsync(op => op.Id == operationId && op.WorkOrderId == id, cancellationToken);
        if (operation is null)
        {
            return NotFound();
        }

        if (operation.Status == "Pending")
        {
            return Conflict(new { message = "Si può registrare una non conformità solo su una fase avviata o completata." });
        }

        WorkOrderUnit? unit = null;
        if (request.WorkOrderUnitId.HasValue)
        {
            unit = await _dbContext.WorkOrderUnits
                .SingleOrDefaultAsync(u => u.Id == request.WorkOrderUnitId.Value && u.WorkOrderId == id, cancellationToken);
            if (unit is null)
            {
                return BadRequest(new { message = "Unità non trovata per questa commessa." });
            }

            if (unit.Status != "Pending")
            {
                return Conflict(new { message = $"L'unità {unit.SerialNumber} è già stata risolta ({unit.Status})." });
            }
        }

        var nonConformity = new NonConformity
        {
            WorkOrderOperationId = operationId,
            Description = request.Description.Trim(),
            ScrapQuantity = request.ScrapQuantity,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ReportedBy = string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim(),
            ReportedByUserId = operatorId,
            WorkOrderUnitId = unit?.Id
        };

        // Registering a non-conformity against a specific unit is what scraps it — no separate step.
        if (unit is not null)
        {
            unit.Status = "Scrapped";
            unit.ResolvedAt = nonConformity.DetectedAt;
        }

        _dbContext.NonConformities.Add(nonConformity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToNonConformityResponse(nonConformity, unit));
    }

    /// <summary>Per-serial traceability: which units this work order produced and what happened to each
    /// — nothing when the quantity wasn't a whole number at creation time (see <see cref="WorkOrderUnit"/>
    /// for why).</summary>
    [HttpGet("{id:guid}/units")]
    public async Task<ActionResult<IEnumerable<WorkOrderUnitResponse>>> GetUnits(Guid id, CancellationToken cancellationToken = default)
    {
        var units = await _dbContext.WorkOrderUnits
            .AsNoTracking()
            .Where(u => u.WorkOrderId == id)
            .OrderBy(u => u.SequenceNumber)
            .ToListAsync(cancellationToken);

        return Ok(units.Select(u => new WorkOrderUnitResponse(u.Id, u.SequenceNumber, u.SerialNumber, u.Status)));
    }

    /// <summary>Full per-serial traceability for one unit: which phases it went through and when, and
    /// which material lots fed it — "what happened to this specific piece" in one call. See
    /// WorkOrderUnitOperation/WorkOrderUnitMaterialLot for how this data is derived.</summary>
    [HttpGet("{id:guid}/units/{unitId:guid}/detail")]
    public async Task<ActionResult<WorkOrderUnitDetailResponse>> GetUnitDetail(
        Guid id, Guid unitId, CancellationToken cancellationToken = default)
    {
        var unit = await _dbContext.WorkOrderUnits
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == unitId && u.WorkOrderId == id, cancellationToken);

        if (unit is null)
        {
            return NotFound();
        }

        var operations = await (
            from unitOperation in _dbContext.WorkOrderUnitOperations.AsNoTracking()
            join operation in _dbContext.WorkOrderOperations.AsNoTracking() on unitOperation.WorkOrderOperationId equals operation.Id
            where unitOperation.WorkOrderUnitId == unitId
            orderby operation.SequenceNumber
            select new WorkOrderUnitOperationResponse(
                operation.Id, operation.SequenceNumber, operation.Name, unitOperation.Status, unitOperation.StartedAt, unitOperation.CompletedAt))
            .ToListAsync(cancellationToken);

        var materialLots = await (
            from unitLot in _dbContext.WorkOrderUnitMaterialLots.AsNoTracking()
            join lot in _dbContext.MaterialLots.AsNoTracking() on unitLot.MaterialLotId equals lot.Id
            where unitLot.WorkOrderUnitId == unitId
            orderby unitLot.CreatedAt
            select new WorkOrderUnitMaterialLotResponse(lot.Id, unitLot.MaterialCode, lot.LotNumber, unitLot.Quantity))
            .ToListAsync(cancellationToken);

        return Ok(new WorkOrderUnitDetailResponse(
            unit.Id, unit.SequenceNumber, unit.SerialNumber, unit.Status, unit.CreatedAt, unit.ResolvedAt, operations, materialLots));
    }

    [HttpGet("{id:guid}/operations/{operationId:guid}/non-conformities")]
    public async Task<ActionResult<IEnumerable<NonConformityResponse>>> GetNonConformities(
        Guid id, Guid operationId, CancellationToken cancellationToken = default)
    {
        var nonConformities = await _dbContext.NonConformities
            .AsNoTracking()
            .Include(n => n.Unit)
            .Where(n => n.WorkOrderOperationId == operationId)
            .OrderByDescending(n => n.DetectedAt)
            .ToListAsync(cancellationToken);

        return Ok(nonConformities.Select(n => ToNonConformityResponse(n, n.Unit)));
    }

    private static NonConformityResponse ToNonConformityResponse(NonConformity nonConformity, WorkOrderUnit? unit) => new(
        nonConformity.Id,
        nonConformity.Description,
        nonConformity.ScrapQuantity,
        nonConformity.Notes,
        nonConformity.DetectedAt,
        nonConformity.ReportedBy,
        nonConformity.WorkOrderUnitId,
        unit?.SerialNumber);

    /// <summary>Day-granularity finite-capacity forward scheduler: assigns each still-open operation
    /// (skips ones already Done) to the earliest calendar day(s) where its work center — matched by name
    /// against the registered <see cref="WorkCenter"/> catalog — has spare daily capacity, accounting for
    /// everything else already scheduled there across every work order, and respecting the routing's
    /// sequence (an operation can't start before the previous one in the same work order ends). Operations
    /// on a work center with no registered capacity (or none matching by name) are placed on the earliest
    /// available day with no capacity check — unconstrained, not blocked. This assigns whole days, not
    /// specific times, and is not a drag-and-drop Gantt — just enough to know which day(s) a phase should
    /// land on and to avoid over-booking a work center across orders.</summary>
    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/schedule")]
    public async Task<ActionResult<WorkOrderResponse>> ScheduleWorkOrder(
        Guid id,
        [FromQuery] DateTime? startFrom = null,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders
            .Include(o => o.Operations)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is "Completed" or "Cancelled")
        {
            return Conflict(new { message = "Non si può pianificare una commessa completata o annullata." });
        }

        var operationsToSchedule = order.Operations
            .Where(op => op.Status != "Done")
            .OrderBy(op => op.SequenceNumber)
            .ToList();

        if (operationsToSchedule.Count == 0)
        {
            return Ok(ToResponse(order));
        }

        // Grouped rather than ToDictionaryAsync: only Code is unique on WorkCenter, so two active
        // records can share a Name (e.g. renamed duplicates) — take the first instead of crashing.
        var workCentersByName = (await _dbContext.WorkCenters
                .AsNoTracking()
                .Where(w => w.IsActive)
                .ToListAsync(cancellationToken))
            .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        // Everything else already scheduled anywhere in the shop (other orders, or other open operations
        // of this one that a previous schedule run already placed), spread evenly across its planned days,
        // so a new run doesn't double-book a work center's daily capacity.
        var otherScheduled = await _dbContext.WorkOrderOperations
            .AsNoTracking()
            .Where(op => op.WorkOrderId != id &&
                         op.Status != "Done" &&
                         op.PlannedStartAt != null && op.PlannedEndAt != null &&
                         op.WorkCenter != null && op.WorkCenter != "")
            .Select(op => new { op.WorkCenter, op.PlannedStartAt, op.PlannedEndAt, op.EstimatedMinutes })
            .ToListAsync(cancellationToken);

        var committed = new Dictionary<string, Dictionary<DateOnly, decimal>>(StringComparer.OrdinalIgnoreCase);
        foreach (var scheduled in otherScheduled)
        {
            var startDay = DateOnly.FromDateTime(scheduled.PlannedStartAt!.Value);
            var endDay = DateOnly.FromDateTime(scheduled.PlannedEndAt!.Value);
            var spanDays = endDay.DayNumber - startDay.DayNumber + 1;
            var perDay = scheduled.EstimatedMinutes / spanDays;

            if (!committed.TryGetValue(scheduled.WorkCenter!, out var byDay))
            {
                byDay = new Dictionary<DateOnly, decimal>();
                committed[scheduled.WorkCenter!] = byDay;
            }

            for (var day = startDay; day <= endDay; day = day.AddDays(1))
            {
                byDay[day] = byDay.GetValueOrDefault(day) + perDay;
            }
        }

        var earliestDay = DateOnly.FromDateTime((startFrom ?? DateTime.UtcNow).Date);

        foreach (var operation in operationsToSchedule)
        {
            var workCenter = operation.WorkCenter is not null && workCentersByName.TryGetValue(operation.WorkCenter, out var match)
                ? match
                : null;

            DateOnly startDay;
            DateOnly endDay;

            if (workCenter is null || workCenter.DailyCapacityMinutes <= 0)
            {
                startDay = earliestDay;
                endDay = earliestDay;
            }
            else
            {
                if (!committed.TryGetValue(workCenter.Name, out var byDay))
                {
                    byDay = new Dictionary<DateOnly, decimal>();
                    committed[workCenter.Name] = byDay;
                }

                var remaining = operation.EstimatedMinutes;
                var day = earliestDay;
                DateOnly? firstDay = null;
                var lastDay = earliestDay;
                while (remaining > 0)
                {
                    var used = byDay.GetValueOrDefault(day);
                    var available = workCenter.DailyCapacityMinutes - used;
                    if (available > 0)
                    {
                        firstDay ??= day;
                        var consumed = Math.Min(available, remaining);
                        byDay[day] = used + consumed;
                        remaining -= consumed;
                        lastDay = day;
                    }

                    day = day.AddDays(1);
                }

                startDay = firstDay ?? earliestDay;
                endDay = lastDay;
            }

            operation.PlannedStartAt = DateTime.SpecifyKind(startDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            operation.PlannedEndAt = DateTime.SpecifyKind(endDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            earliestDay = endDay.AddDays(1);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/generate-withdrawal-slip")]
    public async Task<ActionResult<WorkOrderWithdrawalSlipResponse>> GenerateWithdrawalSlip(
        Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is "Completed" or "Cancelled")
        {
            return Conflict(new { message = "Non è possibile generare una distinta per una commessa completata o annullata." });
        }

        if (order.AreaId is null)
        {
            return BadRequest(new { message = "Assegna un'area alla commessa prima di generare la distinta di prelievo." });
        }

        var bomItems = await _dbContext.BillOfMaterialItems
            .AsNoTracking()
            .Where(item => item.ProductId == order.ProductId)
            .ToListAsync(cancellationToken);

        if (bomItems.Count == 0)
        {
            return BadRequest(new { message = "Il prodotto non ha una distinta base." });
        }

        var currentUserId = GetCurrentUserId();
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        var slip = new WithdrawalSlip
        {
            Code = $"DP-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
            AreaId = order.AreaId.Value,
            RequestedByUserId = currentUserId.Value,
            WorkOrderId = order.Id,
            Notes = $"Generata dalla commessa {order.Code}.",
            Status = "Draft"
        };

        var requestItems = bomItems
            .Select(bomItem => new CreateWithdrawalSlipItemRequest(bomItem.MaterialCode, bomItem.Quantity * order.Quantity, null, null))
            .ToList();

        foreach (var item in await _itemBuilder.BuildAsync(slip.Id, requestItems, cancellationToken))
        {
            slip.Items.Add(item);
        }

        _dbContext.WithdrawalSlips.Add(slip);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WithdrawalSlipCreated",
            EntityType = "WithdrawalSlip",
            EntityId = slip.Id,
            UserName = GetCurrentUserName(),
            Details = $"Distinta {slip.Code} generata dalla commessa {order.Code}."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new WorkOrderWithdrawalSlipResponse(slip.Id, slip.Code));
    }

    /// <summary>Backward genealogy for this work order: every material lot consumed by any withdrawal
    /// slip generated from it — "what batches of raw material went into this production run?".</summary>
    [HttpGet("{id:guid}/material-lots")]
    public async Task<ActionResult<IEnumerable<WorkOrderMaterialLotResponse>>> GetConsumedMaterialLots(
        Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.WorkOrders.AnyAsync(o => o.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var consumed = await (
            from slip in _dbContext.WithdrawalSlips.AsNoTracking()
            where slip.WorkOrderId == id
            join item in _dbContext.WithdrawalItems.AsNoTracking() on slip.Id equals item.WithdrawalSlipId
            join consumption in _dbContext.MaterialLotConsumptions.AsNoTracking() on item.Id equals consumption.WithdrawalItemId
            join lot in _dbContext.MaterialLots.AsNoTracking() on consumption.MaterialLotId equals lot.Id
            orderby lot.MaterialCode, consumption.CreatedAt
            select new WorkOrderMaterialLotResponse(
                lot.Id, lot.MaterialCode, lot.LotNumber, consumption.Quantity, slip.Id, slip.Code))
            .ToListAsync(cancellationToken);

        return Ok(consumed);
    }

    private string? GetCurrentUserName() => User.FindFirstValue(ClaimTypes.Name);

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static WorkOrderResponse ToResponse(WorkOrder order)
    {
        return new WorkOrderResponse(
            order.Id,
            order.Code,
            order.ProductLotNumber,
            order.ProductId,
            order.Quantity,
            order.AreaId,
            order.CustomerReference,
            order.Status,
            order.DueDate,
            order.Notes,
            order.CreatedAt,
            order.ReleasedAt,
            order.CompletedAt,
            order.Operations
                .OrderBy(op => op.SequenceNumber)
                .Select(op => new WorkOrderOperationResponse(
                    op.Id, op.SequenceNumber, op.Name, op.Description, op.WorkCenter,
                    op.EstimatedMinutes, op.Status, op.StartedAt, op.CompletedAt,
                    ActualMinutes(op), PerformanceRatio(op), op.PlannedStartAt, op.PlannedEndAt,
                    op.StartedBy, op.CompletedBy))
                .ToList());
    }

    /// <summary>Minutes actually spent on a finished operation, or null while it's still open — the raw
    /// number a performance ratio is built from.</summary>
    private static decimal? ActualMinutes(WorkOrderOperation operation) =>
        operation.StartedAt.HasValue && operation.CompletedAt.HasValue
            ? (decimal)(operation.CompletedAt.Value - operation.StartedAt.Value).TotalMinutes
            : null;

    /// <summary>EstimatedMinutes / ActualMinutes for a finished operation: above 1 means faster than
    /// planned, below 1 means slower. This is the "Performance" component of OEE in isolation — see
    /// GetDashboard for how it combines with Availability (downtime) and Quality (scrap) into full OEE.</summary>
    private static decimal? PerformanceRatio(WorkOrderOperation operation)
    {
        var actual = ActualMinutes(operation);
        return actual is > 0 ? operation.EstimatedMinutes / actual.Value : null;
    }

    /// <summary>Aggregate KPIs across recent work orders, including full OEE (Availability × Performance
    /// × Quality) now that all three components are tracked, plus simple throughput/on-time counts.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<WorkOrderDashboardResponse>> GetDashboard(
        [FromQuery] int days = 7, [FromQuery] Guid? siteId = null, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Max(1, days));

        var statusCounts = await _dbContext.WorkOrders
            .AsNoTracking()
            .Where(order => siteId == null || (order.Area != null && order.Area.SiteId == siteId))
            .GroupBy(order => order.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var recentlyCompletedOperations = await _dbContext.WorkOrderOperations
            .AsNoTracking()
            .Where(op => op.Status == "Done" && op.CompletedAt >= since && op.StartedAt != null && op.CompletedAt != null &&
                (siteId == null || (op.WorkOrder.Area != null && op.WorkOrder.Area.SiteId == siteId)))
            .ToListAsync(cancellationToken);

        var performanceRatios = recentlyCompletedOperations
            .Select(op => PerformanceRatio(op))
            .Where(ratio => ratio.HasValue)
            .Select(ratio => ratio!.Value)
            .ToList();

        var completedOrders = await _dbContext.WorkOrders
            .AsNoTracking()
            .Where(order => order.Status == "Completed" && order.CompletedAt >= since &&
                (siteId == null || (order.Area != null && order.Area.SiteId == siteId)))
            .ToListAsync(cancellationToken);
        var ordersWithDueDate = completedOrders.Where(order => order.DueDate.HasValue).ToList();
        var onTimeCount = ordersWithDueDate.Count(order => order.CompletedAt <= order.DueDate);

        // Availability (OEE): gross running time of recently completed phases minus the machine stops
        // logged against those same phases.
        var recentOperationIds = recentlyCompletedOperations.Select(op => op.Id).ToHashSet();
        var closedDowntimes = await _dbContext.OperationDowntimes
            .AsNoTracking()
            .Where(downtime => recentOperationIds.Contains(downtime.WorkOrderOperationId) && downtime.EndedAt != null)
            .ToListAsync(cancellationToken);
        var totalDowntimeMinutes = closedDowntimes.Sum(downtime => (decimal)(downtime.EndedAt!.Value - downtime.StartedAt).TotalMinutes);
        var totalGrossMinutes = recentlyCompletedOperations.Sum(op => (decimal)(op.CompletedAt!.Value - op.StartedAt!.Value).TotalMinutes);
        var availabilityRatio = totalGrossMinutes > 0
            ? Math.Max(0, (totalGrossMinutes - totalDowntimeMinutes) / totalGrossMinutes)
            : (decimal?)null;

        // Quality (OEE): for a completed order with per-unit tracking (see WorkOrderUnit — generated
        // only when the ordered quantity was a whole number), this is now an exact good-vs-scrapped unit
        // count, not an approximation. An order without units (its quantity wasn't whole, e.g. 2.5 kg of
        // a bulk product) falls back to comparing logged scrap against its planned quantity — the same
        // approximation this dashboard used before per-unit tracking existed. The two are blended into
        // one ratio, weighted so a handful of untracked bulk orders can't swing the number as much as
        // hundreds of precisely-tracked units.
        var completedOrderIds = completedOrders.Select(order => order.Id).ToHashSet();
        var unitsForCompletedOrders = await _dbContext.WorkOrderUnits
            .AsNoTracking()
            .Where(unit => completedOrderIds.Contains(unit.WorkOrderId))
            .ToListAsync(cancellationToken);
        var ordersWithUnits = unitsForCompletedOrders.Select(unit => unit.WorkOrderId).ToHashSet();
        var totalUnitsTracked = unitsForCompletedOrders.Count;
        var goodUnitsTracked = unitsForCompletedOrders.Count(unit => unit.Status == "Good");

        var ordersWithoutUnits = completedOrders.Where(order => !ordersWithUnits.Contains(order.Id)).ToList();
        var untrackedOrderIds = ordersWithoutUnits.Select(order => order.Id).ToHashSet();
        var scrapForOrdersWithoutUnits = untrackedOrderIds.Count > 0
            ? await _dbContext.NonConformities
                .AsNoTracking()
                .Include(nonConformity => nonConformity.Operation)
                .Where(nonConformity => untrackedOrderIds.Contains(nonConformity.Operation.WorkOrderId))
                .ToListAsync(cancellationToken)
            : [];
        var totalScrapQuantity = scrapForOrdersWithoutUnits.Sum(nonConformity => nonConformity.ScrapQuantity);
        var totalProducedQuantity = ordersWithoutUnits.Sum(order => order.Quantity);

        var totalWeight = totalUnitsTracked + totalProducedQuantity;
        var goodWeight = goodUnitsTracked + Math.Max(0, totalProducedQuantity - totalScrapQuantity);
        var qualityRatio = totalWeight > 0
            ? Math.Max(0, goodWeight / totalWeight)
            : (decimal?)null;

        // Full OEE: Performance is capped at 1 for this composite (finishing faster than estimated
        // shouldn't inflate OEE past 100%, even though the standalone AveragePerformanceRatio above can
        // exceed 1 to show "ahead of schedule").
        var averagePerformanceRatio = performanceRatios.Count > 0 ? performanceRatios.Average() : (decimal?)null;
        var oeeRatio = availabilityRatio.HasValue && averagePerformanceRatio.HasValue && qualityRatio.HasValue
            ? availabilityRatio.Value * Math.Min(1, averagePerformanceRatio.Value) * qualityRatio.Value
            : (decimal?)null;

        return Ok(new WorkOrderDashboardResponse(
            days,
            statusCounts.ToDictionary(entry => entry.Status, entry => entry.Count),
            recentlyCompletedOperations.Count,
            averagePerformanceRatio,
            completedOrders.Count,
            ordersWithDueDate.Count > 0 ? (decimal)onTimeCount / ordersWithDueDate.Count : null,
            totalDowntimeMinutes,
            availabilityRatio,
            totalScrapQuantity,
            qualityRatio,
            oeeRatio));
    }
}

public sealed record CreateWorkOrderRequest(
    Guid ProductId,
    decimal Quantity,
    string? Code,
    Guid? AreaId,
    string? CustomerReference,
    DateTime? DueDate,
    string? Notes,
    string? ProductLotNumber = null);

public sealed record EditWorkOrderRequest(
    decimal Quantity,
    Guid? AreaId,
    string? CustomerReference,
    DateTime? DueDate,
    string? Notes);

public sealed record WorkOrderSummaryResponse(
    Guid Id,
    string Code,
    string ProductLotNumber,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    decimal Quantity,
    string Status,
    DateTime? DueDate,
    DateTime CreatedAt,
    int OperationCount,
    int CompletedOperationCount);

public sealed record WorkOrderResponse(
    Guid Id,
    string Code,
    string ProductLotNumber,
    Guid ProductId,
    decimal Quantity,
    Guid? AreaId,
    string? CustomerReference,
    string Status,
    DateTime? DueDate,
    string? Notes,
    DateTime CreatedAt,
    DateTime? ReleasedAt,
    DateTime? CompletedAt,
    IReadOnlyList<WorkOrderOperationResponse> Operations);

public sealed record WorkOrderOperationResponse(
    Guid Id,
    int SequenceNumber,
    string Name,
    string? Description,
    string? WorkCenter,
    decimal EstimatedMinutes,
    string Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    decimal? ActualMinutes,
    decimal? PerformanceRatio,
    DateTime? PlannedStartAt,
    DateTime? PlannedEndAt,
    string? StartedBy,
    string? CompletedBy);

public sealed record WorkOrderWithdrawalSlipResponse(Guid WithdrawalSlipId, string WithdrawalSlipCode);

public sealed record WorkOrderDashboardResponse(
    int PeriodDays,
    IReadOnlyDictionary<string, int> WorkOrdersByStatus,
    int OperationsCompletedInPeriod,
    decimal? AveragePerformanceRatio,
    int WorkOrdersCompletedInPeriod,
    decimal? OnTimeCompletionRate,
    decimal TotalDowntimeMinutes,
    decimal? AvailabilityRatio,
    decimal TotalScrapQuantity,
    decimal? QualityRatio,
    decimal? OeeRatio);

public sealed record MaterialAvailabilityLineResponse(string MaterialCode, decimal Required, decimal Available, decimal Shortfall);

public sealed record MaterialAvailabilityResponse(bool IsAvailable, IReadOnlyList<MaterialAvailabilityLineResponse> Lines);

public sealed record WorkOrderMaterialLotResponse(
    Guid MaterialLotId,
    string MaterialCode,
    string LotNumber,
    decimal QuantityConsumed,
    Guid WithdrawalSlipId,
    string WithdrawalSlipCode);

public sealed record StartDowntimeRequest(string Reason, string? Notes);

public sealed record OperationDowntimeResponse(
    Guid Id,
    string Reason,
    string? Notes,
    DateTime StartedAt,
    DateTime? EndedAt,
    decimal? DurationMinutes,
    string? ReportedBy,
    string? ClosedBy);

public sealed record RegisterNonConformityRequest(string Description, decimal ScrapQuantity, string? Notes, Guid? WorkOrderUnitId = null);

public sealed record NonConformityResponse(
    Guid Id,
    string Description,
    decimal ScrapQuantity,
    string? Notes,
    DateTime DetectedAt,
    string? ReportedBy,
    Guid? WorkOrderUnitId,
    string? UnitSerialNumber);

public sealed record WorkOrderUnitResponse(Guid Id, int SequenceNumber, string SerialNumber, string Status);

public sealed record WorkOrderUnitOperationResponse(
    Guid OperationId, int SequenceNumber, string Name, string Status, DateTime? StartedAt, DateTime? CompletedAt);

public sealed record WorkOrderUnitMaterialLotResponse(Guid MaterialLotId, string MaterialCode, string LotNumber, decimal Quantity);

public sealed record WorkOrderUnitDetailResponse(
    Guid Id,
    int SequenceNumber,
    string SerialNumber,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    IReadOnlyList<WorkOrderUnitOperationResponse> Operations,
    IReadOnlyList<WorkOrderUnitMaterialLotResponse> MaterialLots);
