using System.Text.Json;
using System.Text.RegularExpressions;
using CrmMes.Core.Data;
using CrmMes.Core.Flows;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Services;

/// <summary>Esegue i flussi configurati dall'Admin quando capita l'evento a cui sono collegati. V1: solo
/// avvisare un ruolo e fermarsi per un'approvazione, in sequenza — vedi STRUMENTO-FLUSSI-DATI.md per cosa
/// manca apposta. Il motore non decide se un evento è successo: lo decide il controller del modulo giusto,
/// chiamando <see cref="PublishAsync"/> dopo che l'azione vera (es. applicare una modifica tecnica) è già
/// salvata — un flusso che fallisce non deve mai far sembrare fallita l'azione che l'ha scatenato.</summary>
public sealed class DataFlowEngine(ApplicationDbContext db)
{
    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    /// <summary>Punto d'ingresso per i controller: pubblica un evento e avvia ogni flusso attivo collegato a
    /// quella chiave. I campi del payload sono quelli dichiarati per l'evento in <see cref="DataFlowEvents"/>;
    /// non è un errore passarne altri o ometterne qualcuno, i segnaposto mancanti restano scritti come sono.</summary>
    public async Task PublishAsync(
        string eventKey, string? entityType, Guid? entityId, IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken = default)
    {
        var definitions = await db.DataFlowDefinitions.AsNoTracking()
            .Include(f => f.Steps)
            .Where(f => f.Enabled && f.TriggerEventKey == eventKey)
            .ToListAsync(cancellationToken);
        if (definitions.Count == 0)
        {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        foreach (var definition in definitions)
        {
            var run = new DataFlowRun
            {
                DataFlowDefinitionId = definition.Id,
                EventKey = eventKey,
                EntityType = entityType,
                EntityId = entityId,
                PayloadJson = payloadJson,
                CurrentStepOrder = definition.Steps.Count == 0 ? 0 : definition.Steps.Min(s => s.Order),
            };
            db.DataFlowRuns.Add(run);
            await ExecuteAsync(run, definition.Steps.OrderBy(s => s.Order).ToList(), payload, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Riprende un'esecuzione ferma in attesa di approvazione. Non torna un errore se non è più in
    /// attesa (approvata/rifiutata due volte dal pannello, o aggiornata da un altro utente nel frattempo): il
    /// chiamante decide se segnalarlo.</summary>
    public async Task<bool> ApproveAsync(Guid runId, string? approvedBy, CancellationToken cancellationToken = default)
    {
        var run = await db.DataFlowRuns.Include(r => r.DataFlowDefinition).ThenInclude(f => f!.Steps)
            .SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null || run.Status != DataFlowRunStatus.WaitingApproval || run.DataFlowDefinition is null)
        {
            return false;
        }

        run.ResolvedBy = approvedBy;
        run.ResolvedAt = DateTime.UtcNow;
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(run.PayloadJson) ?? [];
        var remainingSteps = run.DataFlowDefinition.Steps.OrderBy(s => s.Order).Where(s => s.Order > run.CurrentStepOrder).ToList();
        run.CurrentStepOrder += 1;
        run.Status = DataFlowRunStatus.Running;
        await ExecuteAsync(run, remainingSteps, payload, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RejectAsync(Guid runId, string? rejectedBy, CancellationToken cancellationToken = default)
    {
        var run = await db.DataFlowRuns.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null || run.Status != DataFlowRunStatus.WaitingApproval)
        {
            return false;
        }

        run.Status = DataFlowRunStatus.Rejected;
        run.ResolvedBy = rejectedBy;
        run.ResolvedAt = DateTime.UtcNow;
        run.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Esegue i passi dati in ordine; si ferma (senza consumarlo) sul primo RequireApproval incontrato.
    /// Non chiama SaveChangesAsync: lo fa il chiamante, per restare nella stessa transazione implicita di
    /// PublishAsync/ApproveAsync.</summary>
    private async Task ExecuteAsync(DataFlowRun run, List<DataFlowStep> steps, IReadOnlyDictionary<string, string> payload, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            if (step.Type == DataFlowStepType.RequireApproval)
            {
                run.Status = DataFlowRunStatus.WaitingApproval;
                run.CurrentStepOrder = step.Order;
                run.UpdatedAt = DateTime.UtcNow;
                await NotifyRoleAsync(step.TargetRole, $"In attesa della tua approvazione: {run.EventKey}.", run.Id, cancellationToken);
                return;
            }

            if (step.Type == DataFlowStepType.NotifyRole)
            {
                var message = Render(step.MessageTemplate, payload);
                await NotifyRoleAsync(step.TargetRole, message, run.Id, cancellationToken);
            }

            run.CurrentStepOrder = step.Order;
        }

        run.Status = DataFlowRunStatus.Completed;
        run.UpdatedAt = DateTime.UtcNow;
    }

    private async Task NotifyRoleAsync(string role, string message, Guid runId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return;
        }

        var userIds = await db.Users.AsNoTracking().Where(u => u.IsActive && u.Role == role).Select(u => u.Id).ToListAsync(cancellationToken);
        foreach (var userId in userIds)
        {
            db.Notifications.Add(new Notification { UserId = userId, Message = message, DataFlowRunId = runId });
        }
    }

    /// <summary>Sostituisce <c>{{campo}}</c> col valore del payload; un segnaposto senza valore resta scritto
    /// com'è, così un errore di configurazione si vede subito nella notifica invece di sparire in silenzio.</summary>
    private static string Render(string? template, IReadOnlyDictionary<string, string> payload) =>
        string.IsNullOrWhiteSpace(template)
            ? string.Empty
            : Placeholder.Replace(template, m => payload.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
}
