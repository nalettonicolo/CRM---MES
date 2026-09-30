using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Company registry data, industry and enabled modules. Read by every client at login (to show
/// only the enabled modules); written by an Admin in the first-start configuration.</summary>
[ApiController]
[Authorize]
[Route("api/company-profile")]
public class CompanyProfileController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public CompanyProfileController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>The profile, or IsConfigured=false with every module enabled when nobody has configured
    /// it yet: an existing installation keeps working exactly as before until an Admin configures it.</summary>
    [HttpGet]
    public async Task<ActionResult<CompanyProfileResponse>> GetProfile(CancellationToken cancellationToken = default)
    {
        var profile = await _dbContext.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return Ok(profile is null
            ? new CompanyProfileResponse(false, string.Empty, null, null, null, null, "generic",
                Sectors.Modules.Select(module => module.Key).ToList())
            : ToResponse(profile));
    }

    /// <summary>Sectors and modules the configuration wizard offers, with each sector's default modules.</summary>
    [HttpGet("catalog")]
    public ActionResult<CompanyCatalogResponse> GetCatalog() => Ok(new CompanyCatalogResponse(
        Sectors.All.Select(sector => new SectorResponse(sector.Key, sector.Name, sector.Description, sector.Modules.ToList())).ToList(),
        Sectors.Modules.Select(module => new ModuleResponse(module.Key, module.Name, module.Description, module.SectorSpecific, module.Available)).ToList()));

    [Authorize(Policy = "AdminOnly")]
    [HttpPut]
    public async Task<ActionResult<CompanyProfileResponse>> SaveProfile(
        SaveCompanyProfileRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.CompanyName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "La ragione sociale è obbligatoria." });
        }

        var sector = Sectors.Find(request.Sector);
        if (sector is null)
        {
            return BadRequest(new { message = "Settore non riconosciuto." });
        }

        var unknown = (request.EnabledModules ?? []).Where(key => !Sectors.IsKnownModule(key)).ToList();
        if (unknown.Count > 0)
        {
            return BadRequest(new { message = $"Moduli non riconosciuti: {string.Join(", ", unknown)}." });
        }

        var modules = Sectors.Parse(string.Join(',', request.EnabledModules ?? sector.Modules.ToList()));

        var profile = await _dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            profile = new CompanyProfile { ConfiguredAt = DateTime.UtcNow };
            _dbContext.CompanyProfiles.Add(profile);
        }

        profile.CompanyName = name;
        profile.VatNumber = Clean(request.VatNumber);
        profile.Address = Clean(request.Address);
        profile.Phone = Clean(request.Phone);
        profile.Email = Clean(request.Email);
        profile.Sector = sector.Key;
        profile.EnabledModules = string.Join(',', modules);
        profile.UpdatedAt = DateTime.UtcNow;

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "CompanyProfileSaved",
            EntityType = "CompanyProfile",
            EntityId = profile.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Profilo azienda: {name}, settore {sector.Name}, moduli {profile.EnabledModules}."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(profile));
    }

    private static CompanyProfileResponse ToResponse(CompanyProfile profile) => new(
        true, profile.CompanyName, profile.VatNumber, profile.Address, profile.Phone, profile.Email,
        profile.Sector, Sectors.Parse(profile.EnabledModules).ToList(), profile.Gs1CompanyPrefix);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CompanyProfileResponse(
    bool IsConfigured, string CompanyName, string? VatNumber, string? Address, string? Phone, string? Email,
    string Sector, List<string> EnabledModules, string? Gs1CompanyPrefix = null);

public sealed record SaveCompanyProfileRequest(
    string? CompanyName, string? VatNumber, string? Address, string? Phone, string? Email,
    string? Sector, List<string>? EnabledModules);

public sealed record SectorResponse(string Key, string Name, string Description, List<string> Modules);

public sealed record ModuleResponse(string Key, string Name, string Description, bool SectorSpecific, bool Available);

public sealed record CompanyCatalogResponse(List<SectorResponse> Sectors, List<ModuleResponse> Modules);
