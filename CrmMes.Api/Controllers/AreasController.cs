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

    /// <summary>The departments of a user (the logged-in one without userId): what the terminal and the
    /// phone page show first, and how they name the "my department" filter.</summary>
    [HttpGet("of-user")]
    public async Task<ActionResult<List<UserDepartmentResponse>>> GetUserDepartments(
        [FromQuery] Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var id = userId ?? (Guid.TryParse(User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var parsed) ? parsed : Guid.Empty);
        var areas = await _dbContext.Users.AsNoTracking().Where(u => u.Id == id)
            .SelectMany(u => u.Areas.Where(a => a.IsActive))
            .OrderBy(a => a.Name)
            .Select(a => new UserDepartmentResponse(a.Id, a.Name, a.Code, a.DepartmentType))
            .ToListAsync(cancellationToken);
        return Ok(areas);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Area>>> GetAreas([FromQuery] Guid? siteId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Areas.AsNoTracking();
        if (siteId.HasValue)
        {
            query = query.Where(area => area.SiteId == siteId);
        }

        return Ok(await query.OrderBy(area => area.Name).ToListAsync(cancellationToken));
    }

    /// <summary>Tutto ciò che serve per la schermata di dettaglio di un'area in una sola chiamata
    /// (utenti assegnati, commesse destinate qui, distinte di prelievo verso quest'area) invece di tre
    /// andata/ritorno separate.</summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<AreaDetailResponse>> GetAreaDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var area = await _dbContext.Areas.AsNoTracking()
            .Include(a => a.Users)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (area is null)
        {
            return NotFound();
        }

        var workOrders = await _dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.AreaId == id)
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new AreaWorkOrderResponse(order.Id, order.Code, order.Status, order.DueDate))
            .ToListAsync(cancellationToken);

        var withdrawalSlips = await _dbContext.WithdrawalSlips.AsNoTracking()
            .Where(slip => slip.AreaId == id)
            .OrderByDescending(slip => slip.CreatedAt)
            .Select(slip => new AreaWithdrawalSlipResponse(slip.Id, slip.Code, slip.Status, slip.CreatedAt))
            .ToListAsync(cancellationToken);

        var users = area.Users
            .OrderBy(user => user.Name)
            .Select(user => new AreaUserResponse(user.Id, user.Name, user.Email, user.Role))
            .ToList();

        return Ok(new AreaDetailResponse(area.Id, area.Name, area.Code, area.IsActive, users, workOrders, withdrawalSlips));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{areaId:guid}/users/{userId:guid}")]
    public async Task<IActionResult> UnassignUser(Guid areaId, Guid userId, CancellationToken cancellationToken = default)
    {
        var area = await _dbContext.Areas.Include(a => a.Users).SingleOrDefaultAsync(item => item.Id == areaId, cancellationToken);
        var user = area?.Users.SingleOrDefault(candidate => candidate.Id == userId);
        if (area is null || user is null)
        {
            return NotFound();
        }

        area.Users.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
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

        Site? site = null;
        if (request.SiteId.HasValue)
        {
            site = await _dbContext.Sites.SingleOrDefaultAsync(s => s.Id == request.SiteId && s.IsActive, cancellationToken);
            if (site is null)
            {
                return BadRequest(new { message = "Sede non trovata o non attiva." });
            }
        }

        var area = new Area { Name = name, Code = code, SiteId = site?.Id };
        _dbContext.Areas.Add(area);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Created($"api/areas/{area.Id}", area);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:guid}/site")]
    public async Task<IActionResult> SetAreaSite(Guid id, SetSiteRequest request, CancellationToken cancellationToken = default)
    {
        var area = await _dbContext.Areas.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (area is null)
        {
            return NotFound();
        }

        if (request.SiteId.HasValue &&
            !await _dbContext.Sites.AnyAsync(s => s.Id == request.SiteId && s.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Sede non trovata o non attiva." });
        }

        area.SiteId = request.SiteId;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
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

public sealed record CreateAreaRequest(string? Name, string? Code, Guid? SiteId = null);

public sealed record SetSiteRequest(Guid? SiteId);

public sealed record AreaUserResponse(Guid Id, string Name, string Email, string Role);

public sealed record AreaWorkOrderResponse(Guid Id, string Code, string Status, DateTime? DueDate);

public sealed record AreaWithdrawalSlipResponse(Guid Id, string Code, string Status, DateTime CreatedAt);

public sealed record AreaDetailResponse(
    Guid Id,
    string Name,
    string Code,
    bool IsActive,
    List<AreaUserResponse> Users,
    List<AreaWorkOrderResponse> WorkOrders,
    List<AreaWithdrawalSlipResponse> WithdrawalSlips);

public sealed record UserDepartmentResponse(Guid Id, string Name, string Code, string? DepartmentType);
