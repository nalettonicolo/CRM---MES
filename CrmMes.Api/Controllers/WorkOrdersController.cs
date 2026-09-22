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

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/release")]
    public async Task<ActionResult<WorkOrderResponse>> ReleaseWorkOrder(Guid id, CancellationToken cancellationToken = default)
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

        order.Status = "Released";
        order.ReleasedAt = DateTime.UtcNow;
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "WorkOrderReleased",
            EntityType = "WorkOrder",
            EntityId = order.Id,
            UserName = GetCurrentUserName(),
            Details = $"Commessa {order.Code} rilasciata in produzione."
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(order));
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
                    op.EstimatedMinutes, op.Status, op.StartedAt, op.CompletedAt))
                .ToList());
    }
}

public sealed record CreateWorkOrderRequest(
    Guid ProductId,
    decimal Quantity,
    string? Code,
    Guid? AreaId,
    string? CustomerReference,
    DateTime? DueDate,
    string? Notes);

public sealed record EditWorkOrderRequest(
    decimal Quantity,
    Guid? AreaId,
    string? CustomerReference,
    DateTime? DueDate,
    string? Notes);

public sealed record WorkOrderSummaryResponse(
    Guid Id,
    string Code,
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
    DateTime? CompletedAt);

public sealed record WorkOrderWithdrawalSlipResponse(Guid WithdrawalSlipId, string WithdrawalSlipCode);
