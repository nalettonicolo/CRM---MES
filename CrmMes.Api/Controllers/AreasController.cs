using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/areas")]
public class AreasController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AreasController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Area>>> GetAreas(CancellationToken cancellationToken = default)
    {
        return Ok(await _dbContext.Areas.AsNoTracking().OrderBy(area => area.Name).ToListAsync(cancellationToken));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    public async Task<ActionResult<Area>> CreateArea(
        CreateAreaRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var code = request.Code?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { message = "Nome e codice area sono obbligatori." });
        }

        if (await _dbContext.Areas.AnyAsync(area => area.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Esiste già un'area con questo codice." });
        }

        var area = new Area { Name = name, Code = code };
        _dbContext.Areas.Add(area);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Created($"api/areas/{area.Id}", area);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("{areaId:guid}/users/{userId:guid}")]
    public async Task<IActionResult> AssignUser(
        Guid areaId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var area = await _dbContext.Areas.SingleOrDefaultAsync(item => item.Id == areaId && item.IsActive, cancellationToken);
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        if (area is null || user is null)
        {
            return NotFound(new { message = "Area o utente non trovato o non attivo." });
        }

        if (!await _dbContext.Areas.AnyAsync(item => item.Id == areaId && item.Users.Any(candidate => candidate.Id == userId), cancellationToken))
        {
            area.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }
}

public sealed record CreateAreaRequest(string? Name, string? Code);
