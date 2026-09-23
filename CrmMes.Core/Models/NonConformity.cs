namespace CrmMes.Core.Models;

/// <summary>A logged quality defect discovered during a work order operation — the Quality component
/// of OEE (Availability and Performance already exist elsewhere; this is the last missing piece).
/// Free-text description on purpose, same sector-agnostic pattern as OperationDowntime/WorkCenter: the
/// client suggests a few common values but never forces a fixed taxonomy. Unlike a downtime, a
/// non-conformity isn't a start/end interval — it's a point-in-time record of what was found and how
/// many units it affected.</summary>
public class NonConformity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderOperationId { get; set; }
    public WorkOrderOperation Operation { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public decimal ScrapQuantity { get; set; }
    public string? Notes { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}
