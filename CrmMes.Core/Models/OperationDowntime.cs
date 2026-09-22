namespace CrmMes.Core.Models;

/// <summary>A logged stop during a work order operation, with the operator's stated reason — the
/// Availability component of OEE (Performance and a first cut at Quality already exist elsewhere;
/// this is the piece that was missing). Free-text reason on purpose, same sector-agnostic pattern as
/// WorkCenter: the client suggests a few common values but never forces a fixed taxonomy.</summary>
public class OperationDowntime
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderOperationId { get; set; }
    public WorkOrderOperation Operation { get; set; } = null!;
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null while the stop is still ongoing. An operation can't be completed while it has an
    /// open downtime — see WorkOrdersController.CompleteOperation.</summary>
    public DateTime? EndedAt { get; set; }
}
