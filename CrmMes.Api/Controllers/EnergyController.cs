using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Energy monitoring (module "energy-monitoring"): kWh consumption per machine from the machine's own
/// readings, and energy-efficiency projects with a baseline ("ex ante") and an after ("ex post") period for a
/// tax relief report (e.g. the 2026 hyper-depreciation). Consumption is never typed in: it is always computed
/// from <see cref="MachineEvent.EnergyKwh"/>, the same cumulative meter reading the machine/gateway already
/// sends for Industria 4.0/5.0. Viewing is open to anyone authenticated; creating and editing projects is
/// Admin/Management (policy "Energy") — this feeds a tax document, not a shop-floor task.</summary>
[ApiController]
[Authorize]
[Route("api/energy")]
public class EnergyController(ApplicationDbContext db) : ControllerBase
{
    /// <summary>Consumption of one machine in a period: readings are taken in timestamp order and only the
    /// positive deltas between consecutive ones are summed, so a meter reset or rollover (the reading drops)
    /// is simply ignored instead of producing a negative or wildly wrong total.</summary>
    public static async Task<EnergyConsumptionResult> ComputeConsumptionAsync(ApplicationDbContext db, Guid equipmentId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var readings = await db.MachineEvents.AsNoTracking()
            .Where(e => e.EquipmentId == equipmentId && e.Timestamp >= from && e.Timestamp <= to && e.EnergyKwh != null)
            .OrderBy(e => e.Timestamp)
            .Select(e => e.EnergyKwh!.Value)
            .ToListAsync(cancellationToken);

        if (readings.Count == 0)
        {
            return new EnergyConsumptionResult(null, 0, from, to);
        }

        decimal total = 0;
        for (var i = 1; i < readings.Count; i++)
        {
            var delta = readings[i] - readings[i - 1];
            if (delta > 0)
            {
                total += delta;
            }
        }

        return new EnergyConsumptionResult(total, readings.Count, from, to);
    }

    [HttpGet("equipment/{equipmentId:guid}/consumption")]
    public async Task<ActionResult<EnergyConsumptionResponse>> GetConsumption(
        Guid equipmentId, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return BadRequest(new { message = "La data finale deve essere successiva alla data iniziale." });
        }

        if (!await db.Equipment.AnyAsync(e => e.Id == equipmentId, cancellationToken))
        {
            return NotFound();
        }

        var result = await ComputeConsumptionAsync(db, equipmentId, from.ToUniversalTime(), to.ToUniversalTime(), cancellationToken);
        return Ok(new EnergyConsumptionResponse(result.TotalKwh, result.ReadingCount, result.From, result.To));
    }

    /// <summary>kWh consumed per day over the last N days, for a simple chart; machines with no energy readings
    /// at all are left out rather than shown as a flat zero that could be mistaken for "no consumption".</summary>
    [HttpGet("equipment/{equipmentId:guid}/daily")]
    public async Task<ActionResult<IEnumerable<DailyEnergyResponse>>> GetDaily(
        Guid equipmentId, [FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        if (days is < 1 or > 366)
        {
            return BadRequest(new { message = "L'intervallo deve essere tra 1 e 366 giorni." });
        }

        if (!await db.Equipment.AnyAsync(e => e.Id == equipmentId, cancellationToken))
        {
            return NotFound();
        }

        var from = DateTime.UtcNow.Date.AddDays(-days);
        var readings = await db.MachineEvents.AsNoTracking()
            .Where(e => e.EquipmentId == equipmentId && e.Timestamp >= from && e.EnergyKwh != null)
            .OrderBy(e => e.Timestamp)
            .Select(e => new { e.Timestamp, Energy = e.EnergyKwh!.Value })
            .ToListAsync(cancellationToken);

        var byDay = new SortedDictionary<DateTime, decimal>();
        for (var i = 1; i < readings.Count; i++)
        {
            var delta = readings[i].Energy - readings[i - 1].Energy;
            if (delta > 0)
            {
                var day = readings[i].Timestamp.Date;
                byDay[day] = byDay.GetValueOrDefault(day) + delta;
            }
        }

        return Ok(byDay.Select(kv => new DailyEnergyResponse(kv.Key, kv.Value)));
    }

    /// <summary>kWh attributed to one work order: the machine (or gateway) reports which job code is running
    /// in each reading's WorkOrderCode, so the consumption of the interval before a reading is credited to
    /// that reading's code — the same convention <see cref="Services.MachineStats.Compute"/> uses for time by state.
    /// A job can span more than one machine (different phases, different equipment): all of them are summed.</summary>
    [HttpGet("work-orders/{code}/consumption")]
    public async Task<ActionResult<WorkOrderEnergyResponse>> GetWorkOrderConsumption(string code, CancellationToken cancellationToken = default)
    {
        code = code.Trim();
        if (code.Length == 0)
        {
            return BadRequest(new { message = "Indica il codice della commessa." });
        }

        var equipmentIds = await db.MachineEvents.AsNoTracking()
            .Where(e => e.WorkOrderCode == code)
            .Select(e => e.EquipmentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var byEquipment = new List<WorkOrderEnergyByEquipment>();
        foreach (var equipmentId in equipmentIds)
        {
            var readings = await db.MachineEvents.AsNoTracking()
                .Where(e => e.EquipmentId == equipmentId && e.EnergyKwh != null)
                .OrderBy(e => e.Timestamp)
                .Select(e => new { e.EnergyKwh, e.WorkOrderCode })
                .ToListAsync(cancellationToken);

            decimal total = 0;
            for (var i = 1; i < readings.Count; i++)
            {
                var delta = readings[i].EnergyKwh!.Value - readings[i - 1].EnergyKwh!.Value;
                if (delta > 0 && string.Equals(readings[i - 1].WorkOrderCode, code, StringComparison.OrdinalIgnoreCase))
                {
                    total += delta;
                }
            }

            if (total > 0)
            {
                var name = await db.Equipment.AsNoTracking().Where(e => e.Id == equipmentId).Select(e => e.Name).SingleOrDefaultAsync(cancellationToken);
                byEquipment.Add(new WorkOrderEnergyByEquipment(equipmentId, name ?? "?", total));
            }
        }

        return Ok(new WorkOrderEnergyResponse(code, byEquipment.Count == 0 ? null : byEquipment.Sum(e => e.Kwh), byEquipment));
    }

    // ---------- Efficiency projects ----------

    [HttpGet("projects")]
    public async Task<ActionResult<IEnumerable<EnergyProjectResponse>>> GetProjects([FromQuery] Guid? equipmentId = null, CancellationToken cancellationToken = default)
    {
        var query = db.EnergyProjects.AsNoTracking().Include(p => p.Equipment).AsQueryable();
        if (equipmentId is not null)
        {
            query = query.Where(p => p.EquipmentId == equipmentId);
        }

        var projects = await query.OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        var responses = new List<EnergyProjectResponse>();
        foreach (var project in projects)
        {
            responses.Add(await ToResponseAsync(db, project, cancellationToken));
        }

        return Ok(responses);
    }

    [HttpGet("projects/{id:guid}")]
    public async Task<ActionResult<EnergyProjectResponse>> GetProject(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await db.EnergyProjects.AsNoTracking().Include(p => p.Equipment).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        return Ok(await ToResponseAsync(db, project, cancellationToken));
    }

    [Authorize(Policy = "Energy")]
    [HttpPost("projects")]
    public async Task<ActionResult<EnergyProjectResponse>> CreateProject(CreateEnergyProjectRequest request, CancellationToken cancellationToken = default)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = "Il titolo del progetto è obbligatorio." });
        }

        if (request.BaselineTo <= request.BaselineFrom)
        {
            return BadRequest(new { message = "Il periodo di riferimento (ex ante) deve avere una fine successiva all'inizio." });
        }

        var equipment = await db.Equipment.SingleOrDefaultAsync(e => e.Id == request.EquipmentId, cancellationToken);
        if (equipment is null)
        {
            return BadRequest(new { message = "Macchina non trovata." });
        }

        var project = new EnergyProject
        {
            EquipmentId = equipment.Id,
            Title = title,
            Description = Clean(request.Description),
            BaselineFrom = request.BaselineFrom.ToUniversalTime(),
            BaselineTo = request.BaselineTo.ToUniversalTime(),
            CreatedBy = User.FindFirstValue(ClaimTypes.Name),
        };
        db.EnergyProjects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return Created($"api/energy/projects/{project.Id}", await ToResponseAsync(db, project, cancellationToken, equipment));
    }

    /// <summary>Sets (or clears) the "after" period, once the improvement has run long enough to measure it —
    /// a separate, explicit step from creating the project, since the baseline and the after period are rarely
    /// known at the same time.</summary>
    [Authorize(Policy = "Energy")]
    [HttpPut("projects/{id:guid}/after-period")]
    public async Task<ActionResult<EnergyProjectResponse>> SetAfterPeriod(Guid id, SetAfterPeriodRequest request, CancellationToken cancellationToken = default)
    {
        var project = await db.EnergyProjects.Include(p => p.Equipment).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (request.AfterFrom is null != request.AfterTo is null)
        {
            return BadRequest(new { message = "Imposta entrambe le date, oppure nessuna delle due per annullare il periodo." });
        }

        if (request.AfterFrom is not null && request.AfterTo <= request.AfterFrom)
        {
            return BadRequest(new { message = "Il periodo successivo (ex post) deve avere una fine successiva all'inizio." });
        }

        project.AfterFrom = request.AfterFrom?.ToUniversalTime();
        project.AfterTo = request.AfterTo?.ToUniversalTime();
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(await ToResponseAsync(db, project, cancellationToken));
    }

    [Authorize(Policy = "Energy")]
    [HttpDelete("projects/{id:guid}")]
    public async Task<IActionResult> DeleteProject(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await db.EnergyProjects.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        db.EnergyProjects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static async Task<EnergyProjectResponse> ToResponseAsync(ApplicationDbContext db, EnergyProject project, CancellationToken cancellationToken, Equipment? equipment = null)
    {
        var e = equipment ?? project.Equipment;
        var baseline = await ComputeConsumptionAsync(db, project.EquipmentId, project.BaselineFrom, project.BaselineTo, cancellationToken);
        EnergyConsumptionResult? after = project.AfterFrom is { } from && project.AfterTo is { } to
            ? await ComputeConsumptionAsync(db, project.EquipmentId, from, to, cancellationToken)
            : null;

        // Different-length periods are normalised to a daily average before comparing, so a 30-day baseline
        // against a 90-day after period still gives a meaningful saving instead of comparing raw totals.
        decimal? savingsPercent = null;
        if (baseline.TotalKwh is > 0 && after?.TotalKwh is { } afterKwh)
        {
            var baselineDays = Math.Max(1m, (decimal)(project.BaselineTo - project.BaselineFrom).TotalDays);
            var afterDays = Math.Max(1m, (decimal)((project.AfterTo!.Value - project.AfterFrom!.Value)).TotalDays);
            var baselineDaily = baseline.TotalKwh.Value / baselineDays;
            var afterDaily = afterKwh / afterDays;
            savingsPercent = baselineDaily == 0 ? null : Math.Round((1 - afterDaily / baselineDaily) * 100, 1);
        }

        return new EnergyProjectResponse(project.Id, project.EquipmentId, e.Name, e.Code, project.Title, project.Description,
            project.BaselineFrom, project.BaselineTo, baseline.TotalKwh, baseline.ReadingCount,
            project.AfterFrom, project.AfterTo, after?.TotalKwh, after?.ReadingCount, savingsPercent,
            project.Notes, project.CreatedBy, project.CreatedAt);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record EnergyConsumptionResult(decimal? TotalKwh, int ReadingCount, DateTime From, DateTime To);

public sealed record EnergyConsumptionResponse(decimal? TotalKwh, int ReadingCount, DateTime From, DateTime To);

public sealed record DailyEnergyResponse(DateTime Day, decimal Kwh);

public sealed record WorkOrderEnergyByEquipment(Guid EquipmentId, string EquipmentName, decimal Kwh);

public sealed record WorkOrderEnergyResponse(string WorkOrderCode, decimal? TotalKwh, IReadOnlyList<WorkOrderEnergyByEquipment> ByEquipment);

public sealed record CreateEnergyProjectRequest(Guid EquipmentId, string? Title, string? Description, DateTime BaselineFrom, DateTime BaselineTo);

public sealed record SetAfterPeriodRequest(DateTime? AfterFrom, DateTime? AfterTo);

public sealed record EnergyProjectResponse(
    Guid Id, Guid EquipmentId, string EquipmentName, string EquipmentCode, string Title, string? Description,
    DateTime BaselineFrom, DateTime BaselineTo, decimal? BaselineKwh, int BaselineReadingCount,
    DateTime? AfterFrom, DateTime? AfterTo, decimal? AfterKwh, int? AfterReadingCount, decimal? SavingsPercent,
    string? Notes, string? CreatedBy, DateTime CreatedAt);
