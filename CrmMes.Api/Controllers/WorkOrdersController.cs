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
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.WorkOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(order => order.Status == status.Trim());
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

        order.Status = "Completed";
        order.CompletedAt = DateTime.UtcNow;
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
        Guid id, Guid operationId, CancellationToken cancellationToken = default)
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
        if (order.Status == "Released")
        {
            order.Status = "InProgress";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
    }

    [Authorize]
    [HttpPost("{id:guid}/operations/{operationId:guid}/complete")]
    public async Task<ActionResult<WorkOrderResponse>> CompleteOperation(
        Guid id, Guid operationId, CancellationToken cancellationToken = default)
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

        operation.Status = "Done";
        operation.CompletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
    }

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
                    ActualMinutes(op), PerformanceRatio(op), op.PlannedStartAt, op.PlannedEndAt))
                .ToList());
    }

    /// <summary>Minutes actually spent on a finished operation, or null while it's still open — the raw
    /// number a performance ratio is built from.</summary>
    private static decimal? ActualMinutes(WorkOrderOperation operation) =>
        operation.StartedAt.HasValue && operation.CompletedAt.HasValue
            ? (decimal)(operation.CompletedAt.Value - operation.StartedAt.Value).TotalMinutes
            : null;

    /// <summary>EstimatedMinutes / ActualMinutes for a finished operation: above 1 means faster than
    /// planned, below 1 means slower. This is the "Performance" component of OEE in isolation — full
    /// OEE also needs downtime (Availability) and scrap (Quality) tracking, which this project doesn't
    /// capture yet.</summary>
    private static decimal? PerformanceRatio(WorkOrderOperation operation)
    {
        var actual = ActualMinutes(operation);
        return actual is > 0 ? operation.EstimatedMinutes / actual.Value : null;
    }

    /// <summary>Aggregate KPIs across recent work orders: a first step toward real-time production
    /// visibility. Deliberately not full OEE (Availability × Performance × Quality) — that needs
    /// downtime-reason and scrap/quality capture this project doesn't have yet; this covers the
    /// Performance component plus simple throughput/on-time counts.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<WorkOrderDashboardResponse>> GetDashboard(
        [FromQuery] int days = 7, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Max(1, days));

        var statusCounts = await _dbContext.WorkOrders
            .AsNoTracking()
            .GroupBy(order => order.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var recentlyCompletedOperations = await _dbContext.WorkOrderOperations
            .AsNoTracking()
            .Where(op => op.Status == "Done" && op.CompletedAt >= since && op.StartedAt != null && op.CompletedAt != null)
            .ToListAsync(cancellationToken);

        var performanceRatios = recentlyCompletedOperations
            .Select(op => PerformanceRatio(op))
            .Where(ratio => ratio.HasValue)
            .Select(ratio => ratio!.Value)
            .ToList();

        var completedOrders = await _dbContext.WorkOrders
            .AsNoTracking()
            .Where(order => order.Status == "Completed" && order.CompletedAt >= since)
            .ToListAsync(cancellationToken);
        var ordersWithDueDate = completedOrders.Where(order => order.DueDate.HasValue).ToList();
        var onTimeCount = ordersWithDueDate.Count(order => order.CompletedAt <= order.DueDate);

        return Ok(new WorkOrderDashboardResponse(
            days,
            statusCounts.ToDictionary(entry => entry.Status, entry => entry.Count),
            recentlyCompletedOperations.Count,
            performanceRatios.Count > 0 ? performanceRatios.Average() : null,
            completedOrders.Count,
            ordersWithDueDate.Count > 0 ? (decimal)onTimeCount / ordersWithDueDate.Count : null));
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
    DateTime? PlannedEndAt);

public sealed record WorkOrderWithdrawalSlipResponse(Guid WithdrawalSlipId, string WithdrawalSlipCode);

public sealed record WorkOrderDashboardResponse(
    int PeriodDays,
    IReadOnlyDictionary<string, int> WorkOrdersByStatus,
    int OperationsCompletedInPeriod,
    decimal? AveragePerformanceRatio,
    int WorkOrdersCompletedInPeriod,
    decimal? OnTimeCompletionRate);

public sealed record MaterialAvailabilityLineResponse(string MaterialCode, decimal Required, decimal Available, decimal Shortfall);

public sealed record MaterialAvailabilityResponse(bool IsAvailable, IReadOnlyList<MaterialAvailabilityLineResponse> Lines);

public sealed record WorkOrderMaterialLotResponse(
    Guid MaterialLotId,
    string MaterialCode,
    string LotNumber,
    decimal QuantityConsumed,
    Guid WithdrawalSlipId,
    string WithdrawalSlipCode);
