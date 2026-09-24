using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Anagrafica macchine/asset per la manutenzione (CMMS) — il controparte di WorkCenter, che
/// resta l'unità di capacità/carico; una macchina è opzionalmente collegata al centro di lavoro dove si
/// trova, così un fermo segnalato lì si può ricondurre alla macchina specifica.</summary>
[ApiController]
[Authorize]
[Route("api/equipment")]
public class EquipmentController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public EquipmentController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EquipmentResponse>>> GetEquipment(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Equipment.AsNoTracking().Include(e => e.WorkCenter).AsQueryable();
        if (activeOnly)
        {
            query = query.Where(e => e.IsActive);
        }

        var equipment = await query
            .OrderBy(e => e.Name)
            .Select(e => new EquipmentResponse(e.Id, e.Name, e.Code, e.WorkCenterId, e.WorkCenter != null ? e.WorkCenter.Name : null, e.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(equipment);
    }

    /// <summary>Dettaglio macchina in una sola chiamata: anagrafica + tutti gli interventi di
    /// manutenzione (preventiva e correttiva) registrati su di essa.</summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<EquipmentDetailResponse>> GetEquipmentDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await _dbContext.Equipment.AsNoTracking()
            .Include(e => e.WorkCenter)
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (equipment is null)
        {
            return NotFound();
        }

        var tasks = await _dbContext.MaintenanceTasks.AsNoTracking()
            .Where(t => t.EquipmentId == id)
            .OrderByDescending(t => t.DueDate ?? t.CreatedAt)
            .Select(t => new EquipmentMaintenanceTaskResponse(
                t.Id, t.Title, t.Type, t.Status, t.DueDate, t.CompletedAt))
            .ToListAsync(cancellationToken);

        return Ok(new EquipmentDetailResponse(
            equipment.Id, equipment.Name, equipment.Code, equipment.WorkCenterId,
            equipment.WorkCenter != null ? equipment.WorkCenter.Name : null, equipment.IsActive, tasks));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<EquipmentResponse>> CreateEquipment(
        CreateEquipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var code = request.Code?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { message = "Nome e codice macchina sono obbligatori." });
        }

        if (await _dbContext.Equipment.AnyAsync(e => e.Code == code, cancellationToken))
        {
            return Conflict(new { message = $"Il codice macchina '{code}' esiste già." });
        }

        WorkCenter? workCenter = null;
        if (request.WorkCenterId.HasValue)
        {
            workCenter = await _dbContext.WorkCenters.SingleOrDefaultAsync(w => w.Id == request.WorkCenterId, cancellationToken);
            if (workCenter is null)
            {
                return BadRequest(new { message = "Centro di lavoro non trovato." });
            }
        }

        var equipment = new Equipment { Name = name, Code = code, WorkCenterId = workCenter?.Id };
        _dbContext.Equipment.Add(equipment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new EquipmentResponse(equipment.Id, equipment.Name, equipment.Code, equipment.WorkCenterId, workCenter?.Name, equipment.IsActive);
        return Created($"api/equipment/{equipment.Id}", response);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateEquipment(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await _dbContext.Equipment.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (equipment is null)
        {
            return NotFound();
        }

        equipment.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record CreateEquipmentRequest(string? Name, string? Code, Guid? WorkCenterId);

public sealed record EquipmentResponse(Guid Id, string Name, string Code, Guid? WorkCenterId, string? WorkCenterName, bool IsActive);

public sealed record EquipmentMaintenanceTaskResponse(Guid Id, string Title, string Type, string Status, DateTime? DueDate, DateTime? CompletedAt);

public sealed record EquipmentDetailResponse(
    Guid Id, string Name, string Code, Guid? WorkCenterId, string? WorkCenterName, bool IsActive,
    List<EquipmentMaintenanceTaskResponse> Tasks);
