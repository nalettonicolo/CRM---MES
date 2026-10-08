using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Flows;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Strumento per i flussi di dati dal pannello Admin (v1, vedi STRUMENTO-FLUSSI-DATI.md): l'Admin
/// collega un evento del sistema a una sequenza di passi (avvisare un ruolo, fermarsi per un'approvazione).
/// Configurare i flussi è solo Admin; vedere ed eseguire le proprie approvazioni è aperto a chiunque abbia
/// il ruolo richiesto da quel passo — un flusso che chiede l'ok all'ufficio acquisti non deve passare per
/// l'Admin.</summary>
[ApiController]
[Authorize]
[Route("api/data-flows")]
public class DataFlowsController(ApplicationDbContext db, DataFlowEngine engine) : ControllerBase
{
    /// <summary>Eventi conosciuti, con i campi disponibili come segnaposto nel messaggio di un passo.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("events")]
    public ActionResult<List<DataFlowEventResponse>> GetEvents() =>
        Ok(DataFlowEvents.All.Values
            .Select(e => new DataFlowEventResponse(e.Key, e.Name, e.Fields.Select(f => new DataFlowEventFieldResponse(f.Key, f.Label)).ToList()))
            .ToList());

    [Authorize(Policy = "AdminOnly")]
    [HttpGet]
    public async Task<ActionResult<List<DataFlowResponse>>> GetDefinitions(CancellationToken cancellationToken = default)
    {
        var flows = await db.DataFlowDefinitions.AsNoTracking().Include(f => f.Steps).OrderBy(f => f.Name).ToListAsync(cancellationToken);
        return Ok(flows.Select(ToResponse).ToList());
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    public async Task<ActionResult<DataFlowResponse>> Create(SaveDataFlowRequest request, CancellationToken cancellationToken = default)
    {
        var error = Validate(request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        var flow = new DataFlowDefinition
        {
            Name = request.Name!.Trim(),
            TriggerEventKey = request.TriggerEventKey!,
            Enabled = request.Enabled,
            UpdatedBy = User.FindFirstValue(ClaimTypes.Name),
        };
        ApplySteps(flow, request.Steps!);
        db.DataFlowDefinitions.Add(flow);
        AuditTrail.Add(db, User, "DataFlowCreated", "DataFlowDefinition", flow.Id, $"Flusso \"{flow.Name}\" creato, su evento {flow.TriggerEventKey}.");
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetDefinitions), ToResponse(flow));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DataFlowResponse>> Update(Guid id, SaveDataFlowRequest request, CancellationToken cancellationToken = default)
    {
        var flow = await db.DataFlowDefinitions.Include(f => f.Steps).SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (flow is null)
        {
            return NotFound();
        }

        var error = Validate(request);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        flow.Name = request.Name!.Trim();
        flow.TriggerEventKey = request.TriggerEventKey!;
        flow.Enabled = request.Enabled;
        flow.UpdatedBy = User.FindFirstValue(ClaimTypes.Name);
        flow.UpdatedAt = DateTime.UtcNow;
        db.DataFlowSteps.RemoveRange(flow.Steps);
        flow.Steps.Clear();
        ApplySteps(flow, request.Steps!);
        AuditTrail.Add(db, User, "DataFlowUpdated", "DataFlowDefinition", flow.Id, $"Flusso \"{flow.Name}\" modificato.");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(flow));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var flow = await db.DataFlowDefinitions.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (flow is null)
        {
            return NotFound();
        }

        db.DataFlowDefinitions.Remove(flow);
        AuditTrail.Add(db, User, "DataFlowDeleted", "DataFlowDefinition", flow.Id, $"Flusso \"{flow.Name}\" eliminato.");
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Esecuzioni dei flussi. Un Admin le vede tutte; chiunque altro vede solo quelle in attesa di
    /// un'approvazione del proprio ruolo — non ha bisogno (né diritto) di vedere il resto.</summary>
    [HttpGet("runs")]
    public async Task<ActionResult<List<DataFlowRunResponse>>> GetRuns([FromQuery] string? status, CancellationToken cancellationToken = default)
    {
        var query = db.DataFlowRuns.AsNoTracking().Include(r => r.DataFlowDefinition).ThenInclude(f => f!.Steps).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.Status == status);
        }

        var runs = await query.OrderByDescending(r => r.CreatedAt).Take(300).ToListAsync(cancellationToken);
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (role != "Admin")
        {
            runs = runs.Where(r => PendingStep(r) is { } step && step.TargetRole == role).ToList();
        }

        return Ok(runs.Select(ToRunResponse).ToList());
    }

    [HttpPost("runs/{id:guid}/approve")]
    public async Task<ActionResult<DataFlowRunResponse>> Approve(Guid id, CancellationToken cancellationToken = default)
    {
        var (run, forbidden) = await LoadPendingRunForCurrentUserAsync(id, cancellationToken);
        if (run is null)
        {
            return forbidden ? Forbid() : NotFound();
        }

        await engine.ApproveAsync(id, User.FindFirstValue(ClaimTypes.Name), cancellationToken);
        var reloaded = await db.DataFlowRuns.AsNoTracking().Include(r => r.DataFlowDefinition).ThenInclude(f => f!.Steps)
            .SingleAsync(r => r.Id == id, cancellationToken);
        return Ok(ToRunResponse(reloaded));
    }

    [HttpPost("runs/{id:guid}/reject")]
    public async Task<ActionResult<DataFlowRunResponse>> Reject(Guid id, CancellationToken cancellationToken = default)
    {
        var (run, forbidden) = await LoadPendingRunForCurrentUserAsync(id, cancellationToken);
        if (run is null)
        {
            return forbidden ? Forbid() : NotFound();
        }

        await engine.RejectAsync(id, User.FindFirstValue(ClaimTypes.Name), cancellationToken);
        var reloaded = await db.DataFlowRuns.AsNoTracking().Include(r => r.DataFlowDefinition).ThenInclude(f => f!.Steps)
            .SingleAsync(r => r.Id == id, cancellationToken);
        return Ok(ToRunResponse(reloaded));
    }

    /// <summary>Un'esecuzione ferma si può approvare/rifiutare solo da chi ha il ruolo richiesto dal passo in
    /// attesa, o dall'Admin — mai da chiunque sia autenticato.</summary>
    private async Task<(DataFlowRun? Run, bool Forbidden)> LoadPendingRunForCurrentUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await db.DataFlowRuns.Include(r => r.DataFlowDefinition).ThenInclude(f => f!.Steps).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null || run.Status != DataFlowRunStatus.WaitingApproval)
        {
            return (null, false);
        }

        var role = User.FindFirstValue(ClaimTypes.Role);
        var step = PendingStep(run);
        if (role != "Admin" && (step is null || step.TargetRole != role))
        {
            return (null, true);
        }

        return (run, false);
    }

    private static DataFlowStep? PendingStep(DataFlowRun run) =>
        run.DataFlowDefinition?.Steps.SingleOrDefault(s => s.Order == run.CurrentStepOrder);

    private static string? Validate(SaveDataFlowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Il nome del flusso è obbligatorio.";
        }

        if (!DataFlowEvents.IsKnown(request.TriggerEventKey))
        {
            return "Evento sconosciuto.";
        }

        var steps = request.Steps ?? [];
        if (steps.Count == 0)
        {
            return "Aggiungi almeno un passo.";
        }

        foreach (var step in steps)
        {
            if (!DataFlowStepType.IsKnown(step.Type))
            {
                return $"Tipo di passo sconosciuto: '{step.Type}'.";
            }

            if (string.IsNullOrWhiteSpace(step.TargetRole) || !AccessChannels.Roles.Contains(step.TargetRole))
            {
                return "Ogni passo richiede un ruolo valido.";
            }

            if (step.Type == DataFlowStepType.NotifyRole && string.IsNullOrWhiteSpace(step.MessageTemplate))
            {
                return "Un passo \"Avvisa ruolo\" richiede un messaggio.";
            }
        }

        return null;
    }

    private static void ApplySteps(DataFlowDefinition flow, List<SaveDataFlowStepRequest> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            flow.Steps.Add(new DataFlowStep
            {
                Order = i,
                Type = steps[i].Type,
                TargetRole = steps[i].TargetRole,
                MessageTemplate = steps[i].Type == DataFlowStepType.NotifyRole ? steps[i].MessageTemplate!.Trim() : null,
            });
        }
    }

    private static DataFlowResponse ToResponse(DataFlowDefinition flow) => new(
        flow.Id, flow.Name, flow.TriggerEventKey, flow.Enabled,
        flow.Steps.OrderBy(s => s.Order).Select(s => new DataFlowStepResponse(s.Id, s.Order, s.Type, s.TargetRole, s.MessageTemplate)).ToList());

    private static DataFlowRunResponse ToRunResponse(DataFlowRun run) => new(
        run.Id, run.DataFlowDefinition?.Name ?? "(flusso eliminato)", run.EventKey, run.Status, run.CurrentStepOrder,
        PendingStep(run)?.TargetRole, run.ResolvedBy, run.CreatedAt, run.UpdatedAt);
}

public sealed record DataFlowEventFieldResponse(string Key, string Label);

public sealed record DataFlowEventResponse(string Key, string Name, List<DataFlowEventFieldResponse> Fields);

public sealed record DataFlowStepResponse(Guid Id, int Order, string Type, string TargetRole, string? MessageTemplate);

public sealed record DataFlowResponse(Guid Id, string Name, string TriggerEventKey, bool Enabled, List<DataFlowStepResponse> Steps);

public sealed record DataFlowRunResponse(
    Guid Id, string FlowName, string EventKey, string Status, int CurrentStepOrder, string? WaitingOnRole, string? ResolvedBy, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SaveDataFlowStepRequest(string Type, string TargetRole, string? MessageTemplate);

public sealed record SaveDataFlowRequest(string? Name, string? TriggerEventKey, bool Enabled, List<SaveDataFlowStepRequest>? Steps);
