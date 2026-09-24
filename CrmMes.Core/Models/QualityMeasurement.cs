namespace CrmMes.Core.Models;

/// <summary>One reading taken against a QualityCheckpoint for a specific work order (and, when the work
/// order tracks per-unit serials, optionally a specific unit) — the data behind a certificate of
/// conformity, not a full statistical process control system (no control charts / Cp-Cpk here, just
/// pass/fail against tolerance and a record of what was measured).</summary>
public class QualityMeasurement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CheckpointId { get; set; }
    public QualityCheckpoint Checkpoint { get; set; } = null!;
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public Guid? WorkOrderUnitId { get; set; }
    public WorkOrderUnit? WorkOrderUnit { get; set; }
    public decimal MeasuredValue { get; set; }
    public DateTime MeasuredAt { get; set; } = DateTime.UtcNow;
    public Guid? MeasuredByUserId { get; set; }
    public User? MeasuredByUser { get; set; }
    public string? Notes { get; set; }
}
