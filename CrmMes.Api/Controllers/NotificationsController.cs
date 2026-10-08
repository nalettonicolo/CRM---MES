using CrmMes.Api.Services;
using CrmMes.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Le notifiche in app di un utente — nate dai passi "Avvisa ruolo" o "Richiedi approvazione" dei
/// flussi di dati (vedi DataFlowsController). Ogni utente vede solo le proprie.</summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<NotificationResponse>>> GetMine([FromQuery] bool unreadOnly = false, CancellationToken cancellationToken = default)
    {
        var userId = DepartmentFilter.CurrentUserId(User);
        if (userId is null)
        {
            return Ok(new List<NotificationResponse>());
        }

        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var notifications = await query.OrderByDescending(n => n.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return Ok(notifications.Select(n => new NotificationResponse(n.Id, n.Message, n.DataFlowRunId, n.CreatedAt, n.ReadAt)).ToList());
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = DepartmentFilter.CurrentUserId(User);
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        notification.ReadAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken = default)
    {
        var userId = DepartmentFilter.CurrentUserId(User);
        var unread = await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null).ToListAsync(cancellationToken);
        foreach (var notification in unread)
        {
            notification.ReadAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record NotificationResponse(Guid Id, string Message, Guid? DataFlowRunId, DateTime CreatedAt, DateTime? ReadAt);
