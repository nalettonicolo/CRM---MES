using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>CEI EN 61439 routine verification of a work order's panel (see PanelVerification). Anyone
/// can fill it in and complete it — testing is done on the shop floor — and the name of who completed it
/// is recorded; reopening a completed one is Admin-only.</summary>
[ApiController]
[Authorize]
[Route("api/work-orders/{workOrderId:guid}/panel-verification")]
public class PanelVerificationController : ControllerBase
{
    /// <summary>Routine verifications of CEI EN 61439-1, clause 11.</summary>
    public static readonly IReadOnlyList<(string Clause, string Description)> RoutineChecks =
    [
        ("11.2", "Grado di protezione degli involucri (IP)"),
        ("11.3", "Distanze di isolamento in aria e superficiali"),
        ("11.4", "Protezione contro la scossa elettrica e integrità dei circuiti di protezione"),
        ("11.5", "Installazione dei componenti incorporati"),
        ("11.6", "Circuiti elettrici interni e connessioni (serraggi)"),
        ("11.7", "Terminali per conduttori esterni"),
        ("11.8", "Funzionamento meccanico"),
        ("11.9", "Proprietà dielettriche (prova di tensione applicata o misura d'isolamento)"),
        ("11.10", "Cablaggio, prestazioni operative e funzionamento"),
    ];

    private static readonly string[] Results = ["Pass", "Fail", "NotApplicable"];
    private readonly ApplicationDbContext _dbContext;

    public PanelVerificationController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>The saved verification, or a new unsaved one with the standard checklist (IsSaved=false).</summary>
    [HttpGet]
    public async Task<ActionResult<PanelVerificationResponse>> Get(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var workOrder = await _dbContext.WorkOrders.AsNoTracking().Include(w => w.Product).Include(w => w.Customer)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }

        var verification = await Load(workOrderId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return Ok(ToResponse(verification ?? NewVerification(workOrderId), workOrder, isSaved: verification is not null));
    }

    [HttpPut]
    public async Task<ActionResult<PanelVerificationResponse>> Save(
        Guid workOrderId, SavePanelVerificationRequest request, CancellationToken cancellationToken = default)
    {
        var workOrder = await _dbContext.WorkOrders.AsNoTracking().Include(w => w.Product).Include(w => w.Customer)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }

        var verification = await Load(workOrderId).FirstOrDefaultAsync(cancellationToken);
        if (verification?.Status == "Completed")
        {
            return Conflict(new { message = "Verifica già completata: solo un Admin può riaprirla." });
        }

        if (string.IsNullOrWhiteSpace(request.Standard))
        {
            return BadRequest(new { message = "Indica la norma di prodotto (es. CEI EN 61439-2)." });
        }

        var numbers = new[] { request.RatedVoltage, request.RatedCurrent, request.RatedFrequency, request.ShortTimeWithstandCurrent,
            request.ConditionalShortCircuitCurrent, request.InsulationResistanceMOhm, request.DielectricTestVoltage };
        if (numbers.Any(value => value < 0))
        {
            return BadRequest(new { message = "I valori nominali non possono essere negativi." });
        }

        var checks = request.Checks ?? [];
        if (checks.Any(check => check.Result is not null && !Results.Contains(check.Result)))
        {
            return BadRequest(new { message = "Esito non valido: Superata, Non superata o Non applicabile." });
        }

        if (checks.Any(check => string.IsNullOrWhiteSpace(check.Clause) || string.IsNullOrWhiteSpace(check.Description)))
        {
            return BadRequest(new { message = "Ogni verifica deve avere punto e descrizione." });
        }

        if (verification is null)
        {
            verification = NewVerification(workOrderId);
            verification.Checks.Clear();
            _dbContext.PanelVerifications.Add(verification);
        }
        else
        {
            _dbContext.PanelVerificationChecks.RemoveRange(verification.Checks);
            verification.Checks.Clear();
        }

        verification.Standard = request.Standard.Trim();
        verification.OriginalManufacturer = Clean(request.OriginalManufacturer);
        verification.SystemReference = Clean(request.SystemReference);
        verification.SerialNumber = Clean(request.SerialNumber);
        verification.RatedVoltage = request.RatedVoltage;
        verification.RatedCurrent = request.RatedCurrent;
        verification.RatedFrequency = request.RatedFrequency;
        verification.ShortTimeWithstandCurrent = request.ShortTimeWithstandCurrent;
        verification.ConditionalShortCircuitCurrent = request.ConditionalShortCircuitCurrent;
        verification.IpRating = Clean(request.IpRating);
        verification.InternalSeparation = Clean(request.InternalSeparation);
        verification.EarthingSystem = Clean(request.EarthingSystem);
        verification.InsulationResistanceMOhm = request.InsulationResistanceMOhm;
        verification.DielectricTestVoltage = request.DielectricTestVoltage;
        verification.Notes = Clean(request.Notes);
        verification.UpdatedAt = DateTime.UtcNow;

        var existing = _dbContext.Entry(verification).State != EntityState.Added;
        foreach (var check in checks)
        {
            var entity = new PanelVerificationCheck
            {
                PanelVerificationId = verification.Id,
                Clause = check.Clause!.Trim(),
                Description = check.Description!.Trim(),
                Result = check.Result,
                Notes = Clean(check.Notes)
            };
            verification.Checks.Add(entity);
            if (existing)
            {
                _dbContext.Entry(entity).State = EntityState.Added; // preset Guid key: tell EF it is new
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse((await Load(workOrderId).AsNoTracking().FirstAsync(cancellationToken)), workOrder, isSaved: true));
    }

    /// <summary>Freezes the verification. Needs every check done, none failed, and the rated data the
    /// declaration can't do without (voltage and current).</summary>
    [HttpPost("complete")]
    public async Task<ActionResult<PanelVerificationResponse>> Complete(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var workOrder = await _dbContext.WorkOrders.AsNoTracking().Include(w => w.Product).Include(w => w.Customer)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        var verification = await Load(workOrderId).FirstOrDefaultAsync(cancellationToken);
        if (workOrder is null || verification is null)
        {
            return NotFound();
        }

        if (verification.Status == "Completed")
        {
            return Conflict(new { message = "Verifica già completata." });
        }

        var problems = new List<string>();
        if (verification.Checks.Count == 0 || verification.Checks.Any(check => check.Result is null))
        {
            problems.Add("tutte le verifiche devono avere un esito");
        }

        if (verification.Checks.Any(check => check.Result == "Fail"))
        {
            problems.Add("ci sono verifiche non superate: correggi il quadro e ripeti la prova");
        }

        if (verification.RatedVoltage is null || verification.RatedCurrent is null)
        {
            problems.Add("mancano tensione nominale (Un) e corrente nominale (InA)");
        }

        if (problems.Count > 0)
        {
            return BadRequest(new { message = $"Impossibile completare: {string.Join("; ", problems)}." });
        }

        verification.Status = "Completed";
        verification.CompletedAt = DateTime.UtcNow;
        verification.VerifiedBy = User.FindFirstValue(ClaimTypes.Name);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "PanelVerificationCompleted",
            EntityType = "WorkOrder",
            EntityId = workOrderId,
            UserName = verification.VerifiedBy,
            Details = $"Verifica individuale {verification.Standard} completata per la commessa {workOrder.Code}."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(verification, workOrder, isSaved: true));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("reopen")]
    public async Task<ActionResult<PanelVerificationResponse>> Reopen(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var workOrder = await _dbContext.WorkOrders.AsNoTracking().Include(w => w.Product).Include(w => w.Customer)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        var verification = await Load(workOrderId).FirstOrDefaultAsync(cancellationToken);
        if (workOrder is null || verification is null)
        {
            return NotFound();
        }

        verification.Status = "Draft";
        verification.CompletedAt = null;
        verification.VerifiedBy = null;
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "PanelVerificationReopened",
            EntityType = "WorkOrder",
            EntityId = workOrderId,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"Verifica individuale riaperta per la commessa {workOrder.Code}."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(verification, workOrder, isSaved: true));
    }

    private IQueryable<PanelVerification> Load(Guid workOrderId) =>
        _dbContext.PanelVerifications.Include(v => v.Checks).Where(v => v.WorkOrderId == workOrderId);

    private static PanelVerification NewVerification(Guid workOrderId)
    {
        var verification = new PanelVerification { WorkOrderId = workOrderId, RatedFrequency = 50 };
        foreach (var (clause, description) in RoutineChecks)
        {
            verification.Checks.Add(new PanelVerificationCheck { Clause = clause, Description = description });
        }

        return verification;
    }

    private static PanelVerificationResponse ToResponse(PanelVerification verification, WorkOrder workOrder, bool isSaved) => new(
        isSaved, verification.Status, workOrder.Id, workOrder.Code, workOrder.Product.Code, workOrder.Product.Name,
        workOrder.ProductLotNumber, workOrder.Customer?.Name ?? workOrder.CustomerReference,
        verification.Standard, verification.OriginalManufacturer, verification.SystemReference, verification.SerialNumber,
        verification.RatedVoltage, verification.RatedCurrent, verification.RatedFrequency, verification.ShortTimeWithstandCurrent,
        verification.ConditionalShortCircuitCurrent, verification.IpRating, verification.InternalSeparation, verification.EarthingSystem,
        verification.InsulationResistanceMOhm, verification.DielectricTestVoltage, verification.Notes,
        verification.CompletedAt, verification.VerifiedBy,
        verification.Checks
            .OrderBy(check => ClauseOrder(check.Clause))
            .Select(check => new PanelVerificationCheckResponse(check.Clause, check.Description, check.Result, check.Notes))
            .ToList());

    /// <summary>"11.10" after "11.9": clauses sort by their numeric parts, not as text.</summary>
    private static (int, int, string) ClauseOrder(string clause)
    {
        var parts = clause.Split('.');
        return (parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : int.MaxValue,
                parts.Length > 1 && int.TryParse(parts[1], out var minor) ? minor : int.MaxValue,
                clause);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record PanelVerificationCheckRequest(string? Clause, string? Description, string? Result, string? Notes);

public sealed record SavePanelVerificationRequest(
    string? Standard, string? OriginalManufacturer, string? SystemReference, string? SerialNumber,
    decimal? RatedVoltage, decimal? RatedCurrent, decimal? RatedFrequency, decimal? ShortTimeWithstandCurrent,
    decimal? ConditionalShortCircuitCurrent, string? IpRating, string? InternalSeparation, string? EarthingSystem,
    decimal? InsulationResistanceMOhm, decimal? DielectricTestVoltage, string? Notes,
    List<PanelVerificationCheckRequest>? Checks);

public sealed record PanelVerificationCheckResponse(string Clause, string Description, string? Result, string? Notes);

public sealed record PanelVerificationResponse(
    bool IsSaved, string Status, Guid WorkOrderId, string WorkOrderCode, string ProductCode, string ProductName,
    string? ProductLotNumber, string? CustomerName,
    string Standard, string? OriginalManufacturer, string? SystemReference, string? SerialNumber,
    decimal? RatedVoltage, decimal? RatedCurrent, decimal? RatedFrequency, decimal? ShortTimeWithstandCurrent,
    decimal? ConditionalShortCircuitCurrent, string? IpRating, string? InternalSeparation, string? EarthingSystem,
    decimal? InsulationResistanceMOhm, decimal? DielectricTestVoltage, string? Notes,
    DateTime? CompletedAt, string? VerifiedBy, List<PanelVerificationCheckResponse> Checks);
