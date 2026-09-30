using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Rapportini di intervento (site work reports), see SiteReport. Written by the technicians —
/// any role, typically from a phone or tablet through the web page /tecnici — and signed by the customer
/// on the spot. Signing freezes the report and books its hours on the work order.</summary>
[ApiController]
[Authorize]
[Route("api/site-reports")]
public class SiteReportsController : ControllerBase
{
    private const int MaxSignatureBytes = 300_000;
    private const int MaxCodeAttempts = 3;
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly ApplicationDbContext _dbContext;

    public SiteReportsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Work orders a technician can report on: not completed nor cancelled, with the customer.</summary>
    [HttpGet("open-work-orders")]
    public async Task<ActionResult<IEnumerable<SiteWorkOrderResponse>>> GetOpenWorkOrders(
        [FromQuery] string? q = null, [FromQuery] string? department = null, CancellationToken cancellationToken = default)
    {
        IQueryable<WorkOrder> query = _dbContext.WorkOrders.AsNoTracking()
            .Include(o => o.Product).Include(o => o.Customer)
            .Where(o => o.Status != "Completed" && o.Status != "Cancelled");
        if (string.Equals(department, "mine", StringComparison.OrdinalIgnoreCase))
        {
            query = await DepartmentFilter.ApplyAsync(query, _dbContext, DepartmentFilter.CurrentUserId(User), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(o => o.Code.ToLower().Contains(term) || o.Product.Name.ToLower().Contains(term) ||
                                     (o.Customer != null && o.Customer.Name.ToLower().Contains(term)) ||
                                     (o.CustomerReference != null && o.CustomerReference.ToLower().Contains(term)));
        }

        var orders = await query.OrderBy(o => o.DueDate ?? DateTime.MaxValue).ThenBy(o => o.Code).Take(100).ToListAsync(cancellationToken);
        return Ok(orders.Select(o => new SiteWorkOrderResponse(
            o.Id, o.Code, o.Product.Name, o.Customer?.Name ?? o.CustomerReference, o.Customer?.Address, o.Status, o.DueDate)));
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SiteReportSummaryResponse>>> GetReports(
        [FromQuery] Guid? workOrderId = null, [FromQuery] string? status = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SiteReports.AsNoTracking()
            .Include(r => r.WorkOrder).ThenInclude(o => o.Customer)
            .Include(r => r.Hours)
            .AsQueryable();
        if (workOrderId.HasValue)
        {
            query = query.Where(r => r.WorkOrderId == workOrderId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.Status == status.Trim());
        }

        var reports = await query.OrderByDescending(r => r.WorkDate).ThenByDescending(r => r.CreatedAt).Take(300).ToListAsync(cancellationToken);
        return Ok(reports.Select(r => new SiteReportSummaryResponse(
            r.Id, r.Code, r.WorkOrderId, r.WorkOrder.Code, r.WorkOrder.Customer?.Name ?? r.WorkOrder.CustomerReference,
            r.Status, r.WorkDate, r.Hours.Sum(h => h.Minutes), r.SignedByName, r.SignedAt, r.CreatedBy)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SiteReportResponse>> GetReport(Guid id, CancellationToken cancellationToken = default)
    {
        var report = await LoadAsync(id, tracking: false, cancellationToken);
        return report is null ? NotFound() : Ok(ToResponse(report));
    }

    [HttpPost]
    public async Task<ActionResult<SiteReportResponse>> Create(SaveSiteReportRequest request, CancellationToken cancellationToken = default)
    {
        var workOrder = request.WorkOrderId is { } workOrderId
            ? await _dbContext.WorkOrders.AsNoTracking().Include(o => o.Customer).FirstOrDefaultAsync(o => o.Id == workOrderId, cancellationToken)
            : null;
        if (workOrder is null)
        {
            return BadRequest(new { message = "Scegli la commessa del cantiere." });
        }

        if (workOrder.Status is "Completed" or "Cancelled")
        {
            return Conflict(new { message = "La commessa è chiusa: non si aggiungono rapportini." });
        }

        for (var attempt = 1; ; attempt++)
        {
            var report = new SiteReport
            {
                WorkOrderId = workOrder.Id,
                CreatedBy = User.FindFirstValue(ClaimTypes.Name),
                SiteAddress = workOrder.Customer?.Address
            };
            if (await ApplyAsync(report, request, cancellationToken) is { } error)
            {
                return error;
            }

            report.Code = await NextCodeAsync(report.WorkDate.Year, cancellationToken);
            _dbContext.SiteReports.Add(report);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return CreatedAtAction(nameof(GetReport), new { id = report.Id }, ToResponse((await LoadAsync(report.Id, false, cancellationToken))!));
            }
            catch (DbUpdateException) when (attempt < MaxCodeAttempts)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SiteReportResponse>> Update(Guid id, SaveSiteReportRequest request, CancellationToken cancellationToken = default)
    {
        var report = await LoadAsync(id, tracking: true, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        if (report.Status != "Draft")
        {
            return Conflict(new { message = "Rapportino già firmato: non si modifica, se ne fa uno nuovo." });
        }

        if (await ApplyAsync(report, request, cancellationToken) is { } error)
        {
            return error;
        }

        report.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse((await LoadAsync(id, false, cancellationToken))!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var report = await _dbContext.SiteReports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        if (report.Status != "Draft")
        {
            return Conflict(new { message = "Un rapportino firmato non si elimina." });
        }

        _dbContext.SiteReports.Remove(report);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Customer signature: freezes the report and books each technician's hours on the work
    /// order as labor entries, so they are costed like any other hours.</summary>
    [HttpPost("{id:guid}/sign")]
    public async Task<ActionResult<SiteReportResponse>> Sign(Guid id, SignSiteReportRequest request, CancellationToken cancellationToken = default)
    {
        var report = await LoadAsync(id, tracking: true, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        if (report.Status != "Draft")
        {
            return Conflict(new { message = "Rapportino già firmato." });
        }

        if (string.IsNullOrWhiteSpace(request.SignedByName))
        {
            return BadRequest(new { message = "Scrivi il nome di chi firma per il cliente." });
        }

        if (!IsValidSignature(request.SignatureImage))
        {
            return BadRequest(new { message = "Firma mancante o non valida." });
        }

        if (string.IsNullOrWhiteSpace(report.Description))
        {
            return BadRequest(new { message = "Descrivi i lavori eseguiti prima della firma." });
        }

        report.Status = "Signed";
        report.SignedByName = request.SignedByName.Trim();
        report.SignatureImage = request.SignatureImage;
        report.SignedAt = DateTime.UtcNow;
        report.UpdatedAt = DateTime.UtcNow;
        foreach (var hours in report.Hours)
        {
            _dbContext.LaborEntries.Add(new LaborEntry
            {
                WorkOrderId = report.WorkOrderId,
                WorkCenterId = hours.WorkCenterId,
                OperatorName = hours.TechnicianName,
                Minutes = hours.Minutes,
                WorkDate = report.WorkDate,
                Notes = $"Cantiere, rapportino {report.Code}",
                CreatedBy = User.FindFirstValue(ClaimTypes.Name)
            });
        }

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "SiteReportSigned",
            EntityType = "SiteReport",
            EntityId = report.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Rapportino {report.Code} firmato da {report.SignedByName}: {report.Hours.Sum(h => h.Minutes):0} minuti, {report.Materials.Count} materiali."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(report));
    }

    private async Task<ActionResult?> ApplyAsync(SiteReport report, SaveSiteReportRequest request, CancellationToken cancellationToken)
    {
        var workDate = (request.WorkDate ?? DateTime.UtcNow).Date;
        if (workDate > DateTime.UtcNow.Date.AddDays(1))
        {
            return BadRequest(new { message = "La data dell'intervento non può essere futura." });
        }

        var hours = request.Hours ?? [];
        if (hours.Any(h => string.IsNullOrWhiteSpace(h.TechnicianName) || h.Minutes is <= 0 or > 1440))
        {
            return BadRequest(new { message = "Ogni riga ore vuole il tecnico e da 1 a 1440 minuti." });
        }

        var workCenterIds = hours.Where(h => h.WorkCenterId.HasValue).Select(h => h.WorkCenterId!.Value).Distinct().ToList();
        if (workCenterIds.Count > 0 &&
            await _dbContext.WorkCenters.CountAsync(w => workCenterIds.Contains(w.Id), cancellationToken) != workCenterIds.Count)
        {
            return BadRequest(new { message = "Centro di lavoro non trovato." });
        }

        var materials = request.Materials ?? [];
        if (materials.Any(m => m.Quantity <= 0))
        {
            return BadRequest(new { message = "Le quantità dei materiali devono essere maggiori di zero." });
        }

        var codes = materials.Where(m => !string.IsNullOrWhiteSpace(m.MaterialCode)).Select(m => m.MaterialCode!.Trim()).Distinct().ToList();
        var catalog = await _dbContext.Materials.AsNoTracking().Where(m => codes.Contains(m.Code))
            .ToDictionaryAsync(m => m.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var unknown = codes.Where(code => !catalog.ContainsKey(code)).ToList();
        if (unknown.Count > 0)
        {
            return BadRequest(new { message = $"Materiali non trovati: {string.Join(", ", unknown)}." });
        }

        if (materials.Any(m => string.IsNullOrWhiteSpace(m.MaterialCode) && string.IsNullOrWhiteSpace(m.Description)))
        {
            return BadRequest(new { message = "Ogni materiale vuole un codice o una descrizione." });
        }

        report.WorkDate = DateTime.SpecifyKind(workDate, DateTimeKind.Utc);
        report.SiteAddress = Clean(request.SiteAddress) ?? report.SiteAddress;
        report.Description = request.Description?.Trim() ?? string.Empty;
        report.Notes = Clean(request.Notes);

        var existing = _dbContext.Entry(report).State != EntityState.Detached && _dbContext.Entry(report).State != EntityState.Added;
        _dbContext.SiteReportHours.RemoveRange(report.Hours);
        _dbContext.SiteReportMaterials.RemoveRange(report.Materials);
        report.Hours.Clear();
        report.Materials.Clear();
        foreach (var line in hours)
        {
            var entity = new SiteReportHours { SiteReportId = report.Id, TechnicianName = line.TechnicianName!.Trim(), WorkCenterId = line.WorkCenterId, Minutes = line.Minutes };
            report.Hours.Add(entity);
            if (existing)
            {
                _dbContext.Entry(entity).State = EntityState.Added;
            }
        }

        foreach (var line in materials)
        {
            var code = Clean(line.MaterialCode);
            var material = code is null ? null : catalog[code];
            var entity = new SiteReportMaterial
            {
                SiteReportId = report.Id,
                MaterialCode = material?.Code,
                Description = Clean(line.Description) ?? material!.Name,
                Quantity = line.Quantity,
                Unit = Clean(line.Unit) ?? material?.Unit ?? "pz"
            };
            report.Materials.Add(entity);
            if (existing)
            {
                _dbContext.Entry(entity).State = EntityState.Added;
            }
        }

        return null;
    }

    private async Task<string> NextCodeAsync(int year, CancellationToken cancellationToken)
    {
        var prefix = $"RI-{year}-";
        var codes = await _dbContext.SiteReports.AsNoTracking().Where(r => r.Code.StartsWith(prefix)).Select(r => r.Code).ToListAsync(cancellationToken);
        var last = codes.Select(code => int.TryParse(code[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{last + 1:0000}";
    }

    /// <summary>A PNG data URL of reasonable size whose bytes really are a PNG.</summary>
    public static bool IsValidSignature(string? dataUrl)
    {
        const string header = "data:image/png;base64,";
        if (dataUrl is null || !dataUrl.StartsWith(header, StringComparison.Ordinal) || dataUrl.Length > MaxSignatureBytes * 4 / 3 + header.Length + 4)
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(dataUrl[header.Length..]);
            return bytes.Length is > 100 and <= MaxSignatureBytes && bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private Task<SiteReport?> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = _dbContext.SiteReports
            .Include(r => r.WorkOrder).ThenInclude(o => o.Customer)
            .Include(r => r.WorkOrder).ThenInclude(o => o.Product)
            .Include(r => r.Hours).ThenInclude(h => h.WorkCenter)
            .Include(r => r.Materials)
            .AsSplitQuery();
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    private static SiteReportResponse ToResponse(SiteReport report) => new(
        report.Id, report.Code, report.WorkOrderId, report.WorkOrder.Code, report.WorkOrder.Product.Name,
        report.WorkOrder.Customer?.Name ?? report.WorkOrder.CustomerReference, report.Status, report.WorkDate,
        report.SiteAddress, report.Description, report.Notes, report.SignedByName, report.SignatureImage, report.SignedAt,
        report.CreatedBy, report.CreatedAt,
        report.Hours.OrderBy(h => h.TechnicianName).Select(h => new SiteReportHoursResponse(h.TechnicianName, h.WorkCenterId, h.WorkCenter?.Name, h.Minutes)).ToList(),
        report.Materials.OrderBy(m => m.Description).Select(m => new SiteReportMaterialResponse(m.MaterialCode, m.Description, m.Quantity, m.Unit)).ToList());

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SiteWorkOrderResponse(Guid Id, string Code, string ProductName, string? CustomerName, string? CustomerAddress, string Status, DateTime? DueDate);

public sealed record SiteReportHoursRequest(string? TechnicianName, Guid? WorkCenterId, decimal Minutes);

public sealed record SiteReportMaterialRequest(string? MaterialCode, string? Description, decimal Quantity, string? Unit);

public sealed record SaveSiteReportRequest(
    Guid? WorkOrderId, DateTime? WorkDate, string? SiteAddress, string? Description, string? Notes,
    List<SiteReportHoursRequest>? Hours, List<SiteReportMaterialRequest>? Materials);

public sealed record SignSiteReportRequest(string? SignedByName, string? SignatureImage);

public sealed record SiteReportSummaryResponse(
    Guid Id, string Code, Guid WorkOrderId, string WorkOrderCode, string? CustomerName, string Status, DateTime WorkDate,
    decimal TotalMinutes, string? SignedByName, DateTime? SignedAt, string? CreatedBy);

public sealed record SiteReportHoursResponse(string TechnicianName, Guid? WorkCenterId, string? WorkCenterName, decimal Minutes);

public sealed record SiteReportMaterialResponse(string? MaterialCode, string Description, decimal Quantity, string Unit);

public sealed record SiteReportResponse(
    Guid Id, string Code, Guid WorkOrderId, string WorkOrderCode, string ProductName, string? CustomerName, string Status,
    DateTime WorkDate, string? SiteAddress, string Description, string? Notes, string? SignedByName, string? SignatureImage,
    DateTime? SignedAt, string? CreatedBy, DateTime CreatedAt, List<SiteReportHoursResponse> Hours,
    List<SiteReportMaterialResponse> Materials);
