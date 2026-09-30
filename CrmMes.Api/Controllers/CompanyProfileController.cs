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
                Sectors.Modules.Select(module => module.Key).ToList(), null, ["generic"])
            : ToResponse(profile));
    }

    /// <summary>Sectors and modules the configuration wizard offers, with each sector's default modules.</summary>
    [HttpGet("catalog")]
    public ActionResult<CompanyCatalogResponse> GetCatalog() => Ok(new CompanyCatalogResponse(
        Sectors.All.Select(sector => new SectorResponse(sector.Key, sector.Name, sector.Description, sector.Modules.ToList(),
            Departments.SuggestedFor([sector.Key]))).ToList(),
        Sectors.Modules.Select(module => new ModuleResponse(module.Key, module.Name, module.Description, module.SectorSpecific, module.Available)).ToList(),
        Departments.All.Select(d => new DepartmentCatalogResponse(d.Key, d.Name, d.Description, d.Modules.ToList(),
            d.WorkCenters.Select(w => new WorkCenterTemplateResponse(w.Code, w.Name)).ToList())).ToList()));

    /// <summary>The departments configured so far (areas with a type), for the configuration to show.</summary>
    [HttpGet("structure")]
    public async Task<ActionResult<List<DepartmentResponse>>> GetStructure(CancellationToken cancellationToken = default)
    {
        var areas = await _dbContext.Areas.AsNoTracking().Where(a => a.IsActive && a.DepartmentType != null)
            .OrderBy(a => a.Name).ToListAsync(cancellationToken);
        var counts = await _dbContext.WorkCenters.AsNoTracking().Where(w => w.AreaId != null)
            .GroupBy(w => w.AreaId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return Ok(areas.Select(a => new DepartmentResponse(a.Id, a.DepartmentType!, a.Name, a.Code, a.SiteId,
            counts.FirstOrDefault(c => c.Key == a.Id)?.Count ?? 0)).ToList());
    }

    /// <summary>Creates the departments chosen in the configuration: each one becomes an area with its
    /// type (an existing area with the same name is reused, never duplicated) and, when asked, gets its
    /// typical work centers (skipped when a work center with that code already exists). Idempotent: saving
    /// the configuration twice changes nothing the second time. Removing a department is not done here:
    /// areas hold history (work orders, slips), they are deactivated from the areas screen.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("structure")]
    public async Task<ActionResult<StructureResultResponse>> SaveStructure(
        SaveStructureRequest request, CancellationToken cancellationToken = default)
    {
        var departments = request.Departments ?? [];
        var unknown = departments.Select(d => d.Type).FirstOrDefault(type => Departments.Find(type) is null);
        if (unknown is not null)
        {
            return BadRequest(new { message = $"Reparto non riconosciuto: {unknown}." });
        }

        if (departments.Any(d => string.IsNullOrWhiteSpace(d.Name)))
        {
            return BadRequest(new { message = "Ogni reparto ha bisogno di un nome." });
        }

        var duplicate = departments.GroupBy(d => d.Name!.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            return BadRequest(new { message = $"Il reparto \"{duplicate.Key}\" compare due volte: dai nomi diversi (es. \"Collaudo 2\")." });
        }

        var areas = await _dbContext.Areas.ToListAsync(cancellationToken);
        var workCenters = await _dbContext.WorkCenters.ToListAsync(cancellationToken);
        int createdAreas = 0, updatedAreas = 0, createdWorkCenters = 0;

        foreach (var department in departments)
        {
            var info = Departments.Find(department.Type)!;
            var name = department.Name!.Trim();
            var area = areas.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (area is null)
            {
                area = new Area { Name = name, Code = UniqueCode(info.Key, areas), DepartmentType = info.Key, SiteId = department.SiteId };
                _dbContext.Areas.Add(area);
                areas.Add(area);
                createdAreas++;
            }
            else if (area.DepartmentType != info.Key || !area.IsActive || (department.SiteId is not null && area.SiteId != department.SiteId))
            {
                area.DepartmentType = info.Key;
                area.IsActive = true;
                area.SiteId = department.SiteId ?? area.SiteId;
                updatedAreas++;
            }

            if (!department.CreateWorkCenters)
            {
                continue;
            }

            foreach (var template in info.WorkCenters)
            {
                // The typical code is free: create it. Taken by a work center without a department (made by
                // hand before): adopt it. Taken by another department of the same type (a second assembly
                // line): this department gets its own, with its code as a suffix. Already ours: nothing.
                var existing = workCenters.FirstOrDefault(w => string.Equals(w.Code, template.Code, StringComparison.OrdinalIgnoreCase));
                if (existing is not null && existing.AreaId is null)
                {
                    existing.AreaId = area.Id;
                    continue;
                }

                if (existing is not null && existing.AreaId == area.Id)
                {
                    continue;
                }

                var code = existing is null ? template.Code : $"{template.Code}-{area.Code}".ToUpperInvariant();
                if (workCenters.Any(w => string.Equals(w.Code, code, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var workCenter = new WorkCenter
                {
                    Code = code,
                    Name = existing is null ? template.Name : $"{template.Name} ({name})",
                    DailyCapacityMinutes = 480,
                    AreaId = area.Id,
                    SiteId = area.SiteId,
                };
                _dbContext.WorkCenters.Add(workCenter);
                workCenters.Add(workCenter);
                createdWorkCenters++;
            }
        }

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "CompanyStructureSaved",
            EntityType = "CompanyProfile",
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Reparti: {string.Join(", ", departments.Select(d => d.Name))}. Creati {createdAreas} reparti e {createdWorkCenters} centri di lavoro."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new StructureResultResponse(createdAreas, updatedAreas, createdWorkCenters));
    }

    private static string UniqueCode(string key, List<Area> areas)
    {
        var baseCode = "REP-" + key.ToUpperInvariant();
        var code = baseCode;
        for (var i = 2; areas.Any(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase)); i++)
        {
            code = $"{baseCode}-{i}";
        }

        return code;
    }

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

        // Several activities (a machine builder is mechanics, panels and service at once); the first one is
        // the main sector older clients read. Clients that know only one sector send Sector alone.
        var activityKeys = (request.Activities is { Count: > 0 } ? request.Activities : [request.Sector ?? string.Empty])
            .Select(key => key?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var activities = activityKeys.Select(Sectors.Find).ToList();
        if (activities.Any(a => a is null))
        {
            return BadRequest(new { message = "Settore non riconosciuto." });
        }

        var sector = activities[0]!;

        var unknown = (request.EnabledModules ?? []).Where(key => !Sectors.IsKnownModule(key)).ToList();
        if (unknown.Count > 0)
        {
            return BadRequest(new { message = $"Moduli non riconosciuti: {string.Join(", ", unknown)}." });
        }

        var modules = Sectors.Parse(string.Join(',', request.EnabledModules ?? activities.SelectMany(a => a!.Modules).Distinct().ToList()));

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
        profile.Activities = string.Join(',', activities.Select(a => a!.Key));
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
        profile.Sector, Sectors.Parse(profile.EnabledModules).ToList(), profile.Gs1CompanyPrefix,
        string.IsNullOrWhiteSpace(profile.Activities)
            ? [profile.Sector]
            : profile.Activities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CompanyProfileResponse(
    bool IsConfigured, string CompanyName, string? VatNumber, string? Address, string? Phone, string? Email,
    string Sector, List<string> EnabledModules, string? Gs1CompanyPrefix = null, List<string>? Activities = null);

public sealed record SaveCompanyProfileRequest(
    string? CompanyName, string? VatNumber, string? Address, string? Phone, string? Email,
    string? Sector, List<string>? EnabledModules, List<string>? Activities = null);

public sealed record SectorResponse(string Key, string Name, string Description, List<string> Modules, List<string>? Departments = null);

public sealed record WorkCenterTemplateResponse(string Code, string Name);

public sealed record DepartmentCatalogResponse(string Key, string Name, string Description, List<string> Modules, List<WorkCenterTemplateResponse> WorkCenters);

public sealed record DepartmentResponse(Guid Id, string Type, string Name, string Code, Guid? SiteId, int WorkCenterCount);

public sealed record SaveDepartmentRequest(string? Type, string? Name, Guid? SiteId, bool CreateWorkCenters = true);

public sealed record SaveStructureRequest(List<SaveDepartmentRequest>? Departments);

public sealed record StructureResultResponse(int CreatedDepartments, int UpdatedDepartments, int CreatedWorkCenters);

public sealed record ModuleResponse(string Key, string Name, string Description, bool SectorSpecific, bool Available);

public sealed record CompanyCatalogResponse(List<SectorResponse> Sectors, List<ModuleResponse> Modules, List<DepartmentCatalogResponse>? Departments = null);

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
