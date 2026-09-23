namespace CrmMes.Core.Models;

/// <summary>One individually tracked unit within a work order — the per-serial layer on top of the
/// batch-level lot number (<see cref="WorkOrder.ProductLotNumber"/>): "unit 3 of 5" as its own row, with
/// its own good/scrapped outcome, instead of only knowing the batch's aggregate quantity. Snapshotted at
/// work order creation, one row per unit, but only when the ordered quantity is a whole number — a
/// continuous or bulk product (e.g. 2.5 kg) has nothing discrete to serialize, so no units are generated
/// for it and it keeps using the batch-level approximation instead. Every unit starts Pending; it becomes
/// Scrapped the moment a non-conformity is registered against it specifically, and any unit still Pending
/// when the work order completes becomes Good (nothing flagged it, so it shipped).</summary>
public class WorkOrderUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}
