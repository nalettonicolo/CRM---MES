using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Sedi fisiche della stessa azienda (seconda sede) — una dimensione per filtrare/riportare
/// Aree e Centri di lavoro, non un tenant separato: utenti, materiali, fornitori e prodotti restano
/// condivisi. Vedi Area.SiteId / WorkCenter.SiteId.</summary>
[ApiController]
[Authorize]
[Route("api/sites")]
public class SitesController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public SitesController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SiteResponse>>> GetSites(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Sites.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(site => site.IsActive);
        }

        var sites = await query
            .OrderBy(site => site.Name)
            .Select(site => new SiteResponse(site.Id, site.Name, site.Code, site.Address, site.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(sites);
    }

    /// <summary>Dettaglio sede in una sola chiamata: anagrafica + le aree e i centri di lavoro assegnati.</summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<SiteDetailResponse>> GetSiteDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await _dbContext.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (site is null)
        {
            return NotFound();
        }

        var areas = await _dbContext.Areas.AsNoTracking()
            .Where(a => a.SiteId == id)
            .OrderBy(a => a.Name)
            .Select(a => new SiteAreaResponse(a.Id, a.Name, a.Code, a.IsActive))
            .ToListAsync(cancellationToken);

        var workCenters = await _dbContext.WorkCenters.AsNoTracking()
            .Where(w => w.SiteId == id)
            .OrderBy(w => w.Name)
            .Select(w => new SiteWorkCenterResponse(w.Id, w.Name, w.Code, w.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(new SiteDetailResponse(site.Id, site.Name, site.Code, site.Address, site.IsActive, areas, workCenters));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    public async Task<ActionResult<SiteResponse>> CreateSite(
        CreateSiteRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var code = request.Code?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { message = "Nome e codice sede sono obbligatori." });
        }

        if (await _dbContext.Sites.AnyAsync(site => site.Code == code, cancellationToken))
        {
            return Conflict(new { message = $"Il codice sede '{code}' esiste già." });
        }

        var site = new Site
        {
            Name = name,
            Code = code,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim()
        };

        _dbContext.Sites.Add(site);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new SiteResponse(site.Id, site.Name, site.Code, site.Address, site.IsActive);
        return Created($"api/sites/{site.Id}", response);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateSite(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await _dbContext.Sites.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (site is null)
        {
            return NotFound();
        }

        site.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record CreateSiteRequest(string? Name, string? Code, string? Address);

public sealed record SiteResponse(Guid Id, string Name, string Code, string? Address, bool IsActive);

public sealed record SiteAreaResponse(Guid Id, string Name, string Code, bool IsActive);

public sealed record SiteWorkCenterResponse(Guid Id, string Name, string Code, bool IsActive);

public sealed record SiteDetailResponse(
    Guid Id, string Name, string Code, string? Address, bool IsActive,
    List<SiteAreaResponse> Areas, List<SiteWorkCenterResponse> WorkCenters);
