using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Interventi di manutenzione (CMMS): "Preventiva" pianificata con scadenza (e un'eventuale
/// ricorrenza, che genera automaticamente la prossima occorrenza al completamento) o "Correttiva",
/// tipicamente aperta a fronte di un fermo macchina segnalato in produzione.</summary>
[ApiController]
[Authorize]
[Route("api/maintenance-tasks")]
public class MaintenanceTasksController : ControllerBase
{
    private static readonly string[] ValidTypes = ["Preventiva", "Correttiva"];

    private readonly ApplicationDbContext _dbContext;

    public MaintenanceTasksController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MaintenanceTaskResponse>>> GetTasks(
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] Guid? equipmentId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.MaintenanceTasks.AsNoTracking().Include(t => t.Equipment).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(t => t.Status == status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(t => t.Type == type.Trim());
        }

        if (equipmentId.HasValue)
        {
            query = query.Where(t => t.EquipmentId == equipmentId);
        }

        var tasks = await query
            .OrderBy(t => t.Status == "Pending" ? 0 : 1)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.CreatedAt)
            .Take(200)
            .Select(t => new MaintenanceTaskResponse(
                t.Id, t.EquipmentId, t.Equipment.Name, t.Title, t.Description, t.Type, t.Status,
                t.DueDate, t.CompletedAt, t.RecurrenceDays, t.Notes))
            .ToListAsync(cancellationToken);

        return Ok(tasks);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<MaintenanceTaskResponse>> CreateTask(
        CreateMaintenanceTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var title = request.Title?.Trim();
        var type = request.Type?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = "Il titolo è obbligatorio." });
        }

        if (type is null || !ValidTypes.Contains(type))
        {
            return BadRequest(new { message = "Il tipo deve essere \"Preventiva\" o \"Correttiva\"." });
        }

        var equipment = await _dbContext.Equipment.SingleOrDefaultAsync(e => e.Id == request.EquipmentId && e.IsActive, cancellationToken);
        if (equipment is null)
        {
            return BadRequest(new { message = "Macchina non trovata o non attiva." });
        }

        if (request.OperationDowntimeId.HasValue &&
            !await _dbContext.OperationDowntimes.AnyAsync(d => d.Id == request.OperationDowntimeId, cancellationToken))
        {
            return BadRequest(new { message = "Fermo macchina non trovato." });
        }

        var task = new MaintenanceTask
        {
            EquipmentId = equipment.Id,
            Title = title,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Type = type,
            DueDate = request.DueDate,
            RecurrenceDays = request.RecurrenceDays is > 0 ? request.RecurrenceDays : null,
            OperationDowntimeId = request.OperationDowntimeId
        };

        _dbContext.MaintenanceTasks.Add(task);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created($"api/maintenance-tasks/{task.Id}", ToResponse(task, equipment.Name));
    }

    /// <summary>Segna l'intervento come completato. Se ha una ricorrenza impostata, genera subito la
    /// prossima occorrenza (stessa macchina/titolo/ricorrenza, scadenza = oggi + RecurrenceDays) così un
    /// piano di manutenzione preventiva si autoalimenta invece di dover essere reinserito ogni volta.</summary>
    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<MaintenanceTaskResponse>> CompleteTask(
        Guid id,
        CompleteMaintenanceTaskRequest? request,
        CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.MaintenanceTasks.Include(t => t.Equipment).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        if (task.Status != "Pending")
        {
            return Conflict(new { message = "Questo intervento è già stato completato." });
        }

        if (request?.CompletedByUserId is Guid userId &&
            !await _dbContext.Users.AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return BadRequest(new { message = "Utente non trovato." });
        }

        task.Status = "Completed";
        task.CompletedAt = DateTime.UtcNow;
        task.CompletedByUserId = request?.CompletedByUserId;
        task.Notes = string.IsNullOrWhiteSpace(request?.Notes) ? task.Notes : request.Notes.Trim();

        if (task.RecurrenceDays is int days && task.Type == "Preventiva")
        {
            var nextTask = new MaintenanceTask
            {
                EquipmentId = task.EquipmentId,
                Title = task.Title,
                Description = task.Description,
                Type = "Preventiva",
                DueDate = DateTime.UtcNow.AddDays(days),
                RecurrenceDays = days
            };
            _dbContext.MaintenanceTasks.Add(nextTask);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(task, task.Equipment.Name));
    }

    private static MaintenanceTaskResponse ToResponse(MaintenanceTask task, string equipmentName) => new(
        task.Id, task.EquipmentId, equipmentName, task.Title, task.Description, task.Type, task.Status,
        task.DueDate, task.CompletedAt, task.RecurrenceDays, task.Notes);
}

public sealed record CreateMaintenanceTaskRequest(
    Guid EquipmentId, string? Title, string? Description, string? Type,
    DateTime? DueDate, int? RecurrenceDays, Guid? OperationDowntimeId = null);

public sealed record CompleteMaintenanceTaskRequest(Guid? CompletedByUserId, string? Notes);

public sealed record MaintenanceTaskResponse(
    Guid Id, Guid EquipmentId, string EquipmentName, string Title, string? Description, string Type, string Status,
    DateTime? DueDate, DateTime? CompletedAt, int? RecurrenceDays, string? Notes);
