using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/attendance")]
public class AttendanceController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AttendanceController(ApplicationDbContext dbContext) => _dbContext = dbContext;

    [HttpPost("punch")]
    public async Task<ActionResult<AttendancePunchResponse>> Punch(
        PunchRequest request,
        CancellationToken cancellationToken = default)
    {
        var kind = request.Kind?.Trim();
        if (kind is not AttendancePunchKinds.In and not AttendancePunchKinds.Out)
        {
            return BadRequest(new { message = "Tipo timbratura non valido (In o Out)." });
        }

        var userId = request.UserId;
        if (userId.HasValue)
        {
            if (!User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (!await _dbContext.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            {
                return BadRequest(new { message = "Utente non trovato." });
            }
        }
        else
        {
            userId = CurrentUserId();
            if (userId is null)
            {
                return Unauthorized();
            }
        }

        var punch = new AttendancePunch
        {
            UserId = userId.Value,
            Kind = kind,
            PunchedAt = request.PunchedAt ?? DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        _dbContext.AttendancePunches.Add(punch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var userName = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == punch.UserId)
            .Select(u => u.Name)
            .SingleAsync(cancellationToken);

        return Ok(new AttendancePunchResponse(punch.Id, punch.UserId, userName, punch.PunchedAt, punch.Kind, punch.Notes));
    }

    [HttpGet("today/me")]
    public async Task<ActionResult<IEnumerable<AttendancePunchResponse>>> GetTodayForMe(CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var start = DateTime.UtcNow.Date;
        var end = start.AddDays(1);

        var punches = await MapResponses(
            _dbContext.AttendancePunches.AsNoTracking()
                .Where(p => p.UserId == userId && p.PunchedAt >= start && p.PunchedAt < end)
                .OrderBy(p => p.PunchedAt))
            .ToListAsync(cancellationToken);

        return Ok(punches);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AttendancePunchResponse>>> GetRange(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = DateTime.SpecifyKind((from ?? DateTime.UtcNow.Date.AddDays(-7)).Date, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind((to ?? DateTime.UtcNow.Date.AddDays(1)).Date, DateTimeKind.Utc);
        if (toUtc <= fromUtc)
        {
            return BadRequest(new { message = "Intervallo date non valido." });
        }

        var query = _dbContext.AttendancePunches.AsNoTracking()
            .Where(p => p.PunchedAt >= fromUtc && p.PunchedAt < toUtc);

        if (userId.HasValue)
        {
            query = query.Where(p => p.UserId == userId);
        }

        var punches = await MapResponses(query.OrderBy(p => p.PunchedAt)).ToListAsync(cancellationToken);
        return Ok(punches);
    }

    private IQueryable<AttendancePunchResponse> MapResponses(IQueryable<AttendancePunch> punches) =>
        punches.Join(_dbContext.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new AttendancePunchResponse(
            p.Id, p.UserId, u.Name, p.PunchedAt, p.Kind, p.Notes));

    private Guid? CurrentUserId()
    {
        var value = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}

public sealed record PunchRequest(Guid? UserId, string? Kind, DateTime? PunchedAt, string? Notes);

public sealed record AttendancePunchResponse(
    Guid Id, Guid UserId, string UserName, DateTime PunchedAt, string Kind, string? Notes);
