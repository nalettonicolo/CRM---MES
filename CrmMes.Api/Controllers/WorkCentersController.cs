using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Master data for work centers (reparti/linee) plus a load overview that compares their
/// registered daily capacity against the pending workload from open work order operations. This is
/// deliberately an indicative backlog figure, not a calendar-based finite-capacity schedule: operations
/// don't carry planned dates, so there is no way to know exactly which day a given minute of work would
/// fall on.</summary>
[ApiController]
[Authorize]
[Route("api/work-centers")]
public class WorkCentersController : ControllerBase
{
    private static readonly string[] OpenOperationStatuses = ["Pending", "InProgress"];
    private static readonly string[] OpenWorkOrderStatuses = ["Released", "InProgress"];

    private readonly ApplicationDbContext _dbContext;

    public WorkCentersController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkCenterResponse>>> GetWorkCenters(
        [FromQuery] bool activeOnly = true,
        [FromQuery] Guid? siteId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.WorkCenters.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(w => w.IsActive);
        }

        if (siteId.HasValue)
        {
            query = query.Where(w => w.SiteId == siteId);
        }

        var workCenters = await query
            .OrderBy(w => w.Name)
            .Select(w => new WorkCenterResponse(w.Id, w.Code, w.Name, w.Description, w.DailyCapacityMinutes, w.IsActive, w.SiteId))
            .ToListAsync(cancellationToken);

        return Ok(workCenters);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<WorkCenterResponse>> CreateWorkCenter(
        CreateWorkCenterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Codice e nome sono obbligatori." });
        }

        if (request.DailyCapacityMinutes < 0)
        {
            return BadRequest(new { message = "La capacità giornaliera non può essere negativa." });
        }

        var code = request.Code.Trim();
        if (await _dbContext.WorkCenters.AnyAsync(w => w.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Esiste già un centro di lavoro con questo codice." });
        }

        if (request.SiteId.HasValue &&
            !await _dbContext.Sites.AnyAsync(s => s.Id == request.SiteId && s.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Sede non trovata o non attiva." });
        }

        var workCenter = new WorkCenter
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            DailyCapacityMinutes = request.DailyCapacityMinutes,
            SiteId = request.SiteId
        };

        _dbContext.WorkCenters.Add(workCenter);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new WorkCenterResponse(workCenter.Id, workCenter.Code, workCenter.Name, workCenter.Description, workCenter.DailyCapacityMinutes, workCenter.IsActive, workCenter.SiteId));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkCenterResponse>> EditWorkCenter(
        Guid id,
        EditWorkCenterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Il nome è obbligatorio." });
        }

        if (request.DailyCapacityMinutes < 0)
        {
            return BadRequest(new { message = "La capacità giornaliera non può essere negativa." });
        }

        var workCenter = await _dbContext.WorkCenters.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (workCenter is null)
        {
            return NotFound();
        }

        if (request.SiteId.HasValue &&
            !await _dbContext.Sites.AnyAsync(s => s.Id == request.SiteId && s.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Sede non trovata o non attiva." });
        }

        workCenter.Name = request.Name.Trim();
        workCenter.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        workCenter.DailyCapacityMinutes = request.DailyCapacityMinutes;
        workCenter.SiteId = request.SiteId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new WorkCenterResponse(workCenter.Id, workCenter.Code, workCenter.Name, workCenter.Description, workCenter.DailyCapacityMinutes, workCenter.IsActive, workCenter.SiteId));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateWorkCenter(Guid id, CancellationToken cancellationToken = default)
    {
        var workCenter = await _dbContext.WorkCenters.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (workCenter is null)
        {
            return NotFound();
        }

        workCenter.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Dettaglio di un centro di lavoro: anagrafica + le fasi aperte (Pending/InProgress su
    /// commesse Released/InProgress) accodate su di esso, individuate per nome come nel calcolo del
    /// carico sopra — così si vede esattamente quali commesse stanno aspettando, non solo il totale.</summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<WorkCenterDetailResponse>> GetWorkCenterDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var workCenter = await _dbContext.WorkCenters.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (workCenter is null)
        {
            return NotFound();
        }

        var pendingOperations = await _dbContext.WorkOrderOperations.AsNoTracking()
            .Include(op => op.WorkOrder)
            .Where(op => OpenOperationStatuses.Contains(op.Status) &&
                         OpenWorkOrderStatuses.Contains(op.WorkOrder.Status) &&
                         op.WorkCenter != null && op.WorkCenter.ToLower() == workCenter.Name.ToLower())
            .OrderBy(op => op.WorkOrder.DueDate)
            .Select(op => new WorkCenterPendingOperationResponse(
                op.Id, op.WorkOrder.Code, op.Name, op.SequenceNumber, op.Status, op.EstimatedMinutes, op.WorkOrder.DueDate))
            .ToListAsync(cancellationToken);

        return Ok(new WorkCenterDetailResponse(
            workCenter.Id, workCenter.Code, workCenter.Name, workCenter.Description,
            workCenter.DailyCapacityMinutes, workCenter.IsActive, pendingOperations));
    }

    /// <summary>For each registered work center, the pending minutes from operations still open
    /// (Pending/InProgress) on work orders that are Released or InProgress, matched by name against the
    /// free-text WorkCenter field on the operation. Also lists free-text work centers used on the floor
    /// that have no matching registered capacity, so they surface as "non censito" instead of disappearing.</summary>
    [HttpGet("load")]
    public async Task<ActionResult<IEnumerable<WorkCenterLoadResponse>>> GetLoadOverview(
        [FromQuery] Guid? siteId = null, CancellationToken cancellationToken = default)
    {
        var pendingByName = await _dbContext.WorkOrderOperations
            .Where(op => OpenOperationStatuses.Contains(op.Status) &&
                         OpenWorkOrderStatuses.Contains(op.WorkOrder.Status) &&
                         op.WorkCenter != null && op.WorkCenter != "")
            .GroupBy(op => op.WorkCenter!)
            .Select(group => new
            {
                Name = group.Key,
                PendingMinutes = group.Sum(op => op.EstimatedMinutes),
                OpenOperations = group.Count()
            })
            .ToListAsync(cancellationToken);

        var workCenters = await _dbContext.WorkCenters
            .AsNoTracking()
            .Where(w => w.IsActive && (siteId == null || w.SiteId == siteId))
            .ToListAsync(cancellationToken);

        var results = new List<WorkCenterLoadResponse>();

        foreach (var workCenter in workCenters)
        {
            var pending = pendingByName.SingleOrDefault(p => string.Equals(p.Name, workCenter.Name, StringComparison.OrdinalIgnoreCase));
            var pendingMinutes = pending?.PendingMinutes ?? 0;
            decimal? backlogDays = workCenter.DailyCapacityMinutes > 0 ? pendingMinutes / workCenter.DailyCapacityMinutes : null;

            results.Add(new WorkCenterLoadResponse(
                workCenter.Id,
                workCenter.Code,
                workCenter.Name,
                workCenter.DailyCapacityMinutes,
                pendingMinutes,
                pending?.OpenOperations ?? 0,
                backlogDays));
        }

        // Unregistered work centers have no site to filter by, so they only show up in the unfiltered
        // ("Tutte le sedi") view — otherwise they'd always appear regardless of the selected site.
        if (siteId is null)
        {
            var registeredNames = workCenters.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var unmatched in pendingByName.Where(p => !registeredNames.Contains(p.Name)))
            {
                results.Add(new WorkCenterLoadResponse(null, null, unmatched.Name, null, unmatched.PendingMinutes, unmatched.OpenOperations, null));
            }
        }

        return Ok(results.OrderByDescending(r => r.PendingMinutes));
    }
}

public sealed record CreateWorkCenterRequest(string Code, string Name, string? Description, decimal DailyCapacityMinutes, Guid? SiteId = null);
public sealed record EditWorkCenterRequest(string Name, string? Description, decimal DailyCapacityMinutes, Guid? SiteId = null);
public sealed record WorkCenterResponse(Guid Id, string Code, string Name, string? Description, decimal DailyCapacityMinutes, bool IsActive, Guid? SiteId = null);

/// <summary>Id/Code are null for a work center used on the floor (free text on a routing step / operation)
/// that has no matching registered WorkCenter record — its capacity and backlog-in-days are unknown.</summary>
public sealed record WorkCenterLoadResponse(
    Guid? Id,
    string? Code,
    string Name,
    decimal? DailyCapacityMinutes,
    decimal PendingMinutes,
    int OpenOperations,
    decimal? BacklogDays);

public sealed record WorkCenterPendingOperationResponse(
    Guid OperationId, string WorkOrderCode, string OperationName, int SequenceNumber, string Status, decimal EstimatedMinutes, DateTime? WorkOrderDueDate);

public sealed record WorkCenterDetailResponse(
    Guid Id, string Code, string Name, string? Description, decimal DailyCapacityMinutes, bool IsActive,
    List<WorkCenterPendingOperationResponse> PendingOperations);
