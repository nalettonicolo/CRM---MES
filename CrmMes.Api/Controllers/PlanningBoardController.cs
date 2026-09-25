using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>The hand-painted production planning board: rows are PlanningProject (a machine/build being
/// tracked at a coarse weekly level, separate from a Commessa's own phase-by-phase tracking), columns are
/// weeks, and each cell can hold one or more PlanningCategory paints — user-defined phase categories, not
/// a fixed enum, so the board's vocabulary matches however this company actually organizes a build.
/// Viewing is open to any authenticated user (same as the rest of the app); painting/editing requires
/// Admin — the reference tool this was modeled on gated editing behind a separate shared password, but
/// this app already has real per-user authentication, so reusing its role system is both simpler and
/// more secure than adding a second, weaker gate next to it.</summary>
[ApiController]
[Route("api/planning-board")]
[Authorize]
public class PlanningBoardController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public PlanningBoardController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // ---- Categories ----

    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<PlanningCategoryResponse>>> GetCategories(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PlanningCategories.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var categories = await query
            .OrderBy(c => c.SequenceNumber)
            .Select(c => new PlanningCategoryResponse(c.Id, c.Code, c.Name, c.ColorHex, c.SequenceNumber, c.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("categories")]
    public async Task<ActionResult<PlanningCategoryResponse>> CreateCategory(
        CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code?.Trim();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Codice e nome sono obbligatori." });
        }

        if (await _dbContext.PlanningCategories.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Esiste già una categoria con questo codice." });
        }

        var maxSequence = await _dbContext.PlanningCategories.Select(c => (int?)c.SequenceNumber).MaxAsync(cancellationToken) ?? 0;
        var category = new PlanningCategory
        {
            Code = code,
            Name = name,
            ColorHex = string.IsNullOrWhiteSpace(request.ColorHex) ? "#8C7F6A" : request.ColorHex.Trim(),
            SequenceNumber = maxSequence + 1
        };

        _dbContext.PlanningCategories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new PlanningCategoryResponse(category.Id, category.Code, category.Name, category.ColorHex, category.SequenceNumber, category.IsActive));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("categories/{id:guid}")]
    public async Task<ActionResult<PlanningCategoryResponse>> EditCategory(
        Guid id, EditCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Il nome è obbligatorio." });
        }

        var category = await _dbContext.PlanningCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.Name = name;
        if (!string.IsNullOrWhiteSpace(request.ColorHex))
        {
            category.ColorHex = request.ColorHex.Trim();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new PlanningCategoryResponse(category.Id, category.Code, category.Name, category.ColorHex, category.SequenceNumber, category.IsActive));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeactivateCategory(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _dbContext.PlanningCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("categories/reorder")]
    public async Task<IActionResult> ReorderCategories(ReorderRequest request, CancellationToken cancellationToken = default)
    {
        var categories = await _dbContext.PlanningCategories
            .Where(c => request.OrderedIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        for (var i = 0; i < request.OrderedIds.Count; i++)
        {
            if (categories.TryGetValue(request.OrderedIds[i], out var category))
            {
                category.SequenceNumber = i;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---- Projects ----

    [HttpGet("projects")]
    public async Task<ActionResult<IEnumerable<PlanningProjectResponse>>> GetProjects(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PlanningProjects.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        var projects = await query
            .OrderBy(p => p.SequenceNumber)
            .Select(p => new PlanningProjectResponse(p.Id, p.Name, p.Status, p.Notes, p.SequenceNumber, p.IsActive, p.WorkOrderId))
            .ToListAsync(cancellationToken);

        return Ok(projects);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("projects")]
    public async Task<ActionResult<PlanningProjectResponse>> CreateProject(
        CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Il nome è obbligatorio." });
        }

        if (request.WorkOrderId.HasValue && !await _dbContext.WorkOrders.AnyAsync(o => o.Id == request.WorkOrderId, cancellationToken))
        {
            return BadRequest(new { message = "Commessa collegata non trovata." });
        }

        var maxSequence = await _dbContext.PlanningProjects.Select(p => (int?)p.SequenceNumber).MaxAsync(cancellationToken) ?? 0;
        var project = new PlanningProject
        {
            Name = name,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "InValutazione" : request.Status.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            WorkOrderId = request.WorkOrderId,
            SequenceNumber = maxSequence + 1
        };

        _dbContext.PlanningProjects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new PlanningProjectResponse(project.Id, project.Name, project.Status, project.Notes, project.SequenceNumber, project.IsActive, project.WorkOrderId));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("projects/{id:guid}")]
    public async Task<ActionResult<PlanningProjectResponse>> EditProject(
        Guid id, EditProjectRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Il nome è obbligatorio." });
        }

        var project = await _dbContext.PlanningProjects.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (request.WorkOrderId.HasValue && !await _dbContext.WorkOrders.AnyAsync(o => o.Id == request.WorkOrderId, cancellationToken))
        {
            return BadRequest(new { message = "Commessa collegata non trovata." });
        }

        project.Name = name;
        project.Status = string.IsNullOrWhiteSpace(request.Status) ? project.Status : request.Status.Trim();
        project.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        project.WorkOrderId = request.WorkOrderId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new PlanningProjectResponse(project.Id, project.Name, project.Status, project.Notes, project.SequenceNumber, project.IsActive, project.WorkOrderId));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("projects/{id:guid}")]
    public async Task<IActionResult> DeactivateProject(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _dbContext.PlanningProjects.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        project.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("projects/reorder")]
    public async Task<IActionResult> ReorderProjects(ReorderRequest request, CancellationToken cancellationToken = default)
    {
        var projects = await _dbContext.PlanningProjects
            .Where(p => request.OrderedIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        for (var i = 0; i < request.OrderedIds.Count; i++)
        {
            if (projects.TryGetValue(request.OrderedIds[i], out var project))
            {
                project.SequenceNumber = i;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---- Cells ----

    /// <summary>Every painted cell across every active project in the given week range, in one call —
    /// the client builds the whole grid from this plus GetProjects, instead of one request per row.</summary>
    [HttpGet("cells")]
    public async Task<ActionResult<IEnumerable<PlanningCellResponse>>> GetCells(
        [FromQuery] DateTime from, [FromQuery] int weeks, CancellationToken cancellationToken = default)
    {
        weeks = Math.Clamp(weeks, 1, 260);
        var rangeStart = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var rangeEnd = rangeStart.AddDays(weeks * 7);

        var cells = await _dbContext.PlanningCells
            .AsNoTracking()
            .Where(c => c.WeekStart >= rangeStart && c.WeekStart < rangeEnd && c.Project.IsActive)
            .Select(c => new PlanningCellResponse(c.PlanningProjectId, c.PlanningCategoryId, c.WeekStart))
            .ToListAsync(cancellationToken);

        return Ok(cells);
    }

    /// <summary>Paints one category across a run of weeks for one project — the server side of a
    /// click-drag "Compila" gesture. Upsert: weeks already painted with this category are left alone
    /// instead of erroring on the unique index.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("cells")]
    public async Task<IActionResult> PaintCells(PaintCellsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.WeekStarts is null || request.WeekStarts.Count == 0)
        {
            return BadRequest(new { message = "Indica almeno una settimana." });
        }

        if (!await _dbContext.PlanningProjects.AnyAsync(p => p.Id == request.ProjectId, cancellationToken))
        {
            return BadRequest(new { message = "Progetto non trovato." });
        }

        if (!await _dbContext.PlanningCategories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            return BadRequest(new { message = "Categoria non trovata." });
        }

        var weekStarts = request.WeekStarts.Select(w => DateTime.SpecifyKind(w.Date, DateTimeKind.Utc)).Distinct().ToList();
        var existing = await _dbContext.PlanningCells
            .Where(c => c.PlanningProjectId == request.ProjectId && c.PlanningCategoryId == request.CategoryId && weekStarts.Contains(c.WeekStart))
            .Select(c => c.WeekStart)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();

        foreach (var week in weekStarts.Where(week => !existingSet.Contains(week)))
        {
            _dbContext.PlanningCells.Add(new PlanningCell
            {
                PlanningProjectId = request.ProjectId,
                PlanningCategoryId = request.CategoryId,
                WeekStart = week
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Clears every category painted on the given weeks for one project — the server side of
    /// the "Cancella" tool. Clears the whole cell (all overlapping categories), matching what the
    /// reference tool's eraser does; painting a specific category back is a separate "Compila" call.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("cells")]
    public async Task<IActionResult> ClearCells(ClearCellsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.WeekStarts is null || request.WeekStarts.Count == 0)
        {
            return BadRequest(new { message = "Indica almeno una settimana." });
        }

        var weekStarts = request.WeekStarts.Select(w => DateTime.SpecifyKind(w.Date, DateTimeKind.Utc)).ToList();
        var cells = await _dbContext.PlanningCells
            .Where(c => c.PlanningProjectId == request.ProjectId && weekStarts.Contains(c.WeekStart))
            .ToListAsync(cancellationToken);

        _dbContext.PlanningCells.RemoveRange(cells);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record PlanningCategoryResponse(Guid Id, string Code, string Name, string ColorHex, int SequenceNumber, bool IsActive);
public sealed record CreateCategoryRequest(string? Code, string? Name, string? ColorHex);
public sealed record EditCategoryRequest(string? Name, string? ColorHex);

public sealed record PlanningProjectResponse(Guid Id, string Name, string Status, string? Notes, int SequenceNumber, bool IsActive, Guid? WorkOrderId);
public sealed record CreateProjectRequest(string? Name, string? Status, string? Notes, Guid? WorkOrderId);
public sealed record EditProjectRequest(string? Name, string? Status, string? Notes, Guid? WorkOrderId);

public sealed record ReorderRequest(List<Guid> OrderedIds);

public sealed record PlanningCellResponse(Guid ProjectId, Guid CategoryId, DateTime WeekStart);
public sealed record PaintCellsRequest(Guid ProjectId, Guid CategoryId, List<DateTime> WeekStarts);
public sealed record ClearCellsRequest(Guid ProjectId, List<DateTime> WeekStarts);
