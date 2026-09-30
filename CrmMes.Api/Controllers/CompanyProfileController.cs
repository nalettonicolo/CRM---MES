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

    /// <summary>The Admin's channel settings with everything a settings screen needs to show them.
    /// Readable by every logged-in user: the menus of every client are built from it.</summary>
    [HttpGet("access")]
    public async Task<ActionResult<AccessChannelsResponse>> GetAccess(CancellationToken cancellationToken = default)
    {
        var stored = await _dbContext.CompanyProfiles.AsNoTracking().Select(p => p.AccessChannels).FirstOrDefaultAsync(cancellationToken);
        return Ok(ToAccessResponse(AccessChannels.Parse(stored)));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("access")]
    public async Task<ActionResult<AccessChannelsResponse>> SaveAccess(
        SaveAccessChannelsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = new AccessChannels.Settings
        {
            Channels = request.Channels ?? [],
            Roles = request.Roles ?? [],
            Areas = request.Areas ?? [],
        };
        var serialized = AccessChannels.Serialize(settings);
        settings = AccessChannels.Parse(serialized);
        var error = AccessChannels.Validate(settings);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        var profile = await _dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return Conflict(new { message = "Configura prima l'azienda (ragione sociale e settore)." });
        }

        profile.AccessChannels = serialized;
        profile.UpdatedAt = DateTime.UtcNow;
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "AccessChannelsSaved",
            EntityType = "CompanyProfile",
            EntityId = profile.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Canali di accesso: {serialized}"
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToAccessResponse(settings));
    }

    /// <summary>Areas to show on a channel: modules switched on and allowed there by the Admin.</summary>
    [HttpGet("areas")]
    public async Task<ActionResult<List<string>>> GetAreas([FromQuery] string? channel, CancellationToken cancellationToken = default)
    {
        var normalized = AccessChannels.NormalizeChannel(channel);
        if (normalized is null || !AccessChannels.AreaChannels.Contains(normalized))
        {
            return BadRequest(new { message = "Canale non valido: desktop o web." });
        }

        var profile = await _dbContext.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var modules = profile is null
            ? Sectors.Modules.Select(module => module.Key).ToList()
            : Sectors.Parse(profile.EnabledModules).ToList();
        return Ok(AccessChannels.AreasFor(AccessChannels.Parse(profile?.AccessChannels), modules, normalized));
    }

    private static AccessChannelsResponse ToAccessResponse(AccessChannels.Settings settings) => new(
        settings.Channels,
        settings.Roles,
        settings.Areas,
        AccessChannels.Areas.Select(area => new AccessAreaResponse(area.Key, area.Name, area.Module)).ToList(),
        AccessChannels.Roles.ToList());

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

public sealed record AccessAreaResponse(string Key, string Name, string? Module);

public sealed record AccessChannelsResponse(
    List<string> Channels,
    Dictionary<string, List<string>> Roles,
    Dictionary<string, List<string>> Areas,
    List<AccessAreaResponse> KnownAreas,
    List<string> KnownRoles);

public sealed record SaveAccessChannelsRequest(
    List<string>? Channels,
    Dictionary<string, List<string>>? Roles,
    Dictionary<string, List<string>>? Areas);
