namespace CrmMes.Core.Models;

/// <summary>Per-unit projection of a work order's phase progress — mirrors the batch-level
/// WorkOrderOperation start/complete timing onto each individual WorkOrderUnit, so "which phases has
/// unit 3 of 5 actually gone through, and when" is queryable instead of only knowing the batch's overall
/// phase status. Generated Pending for every (unit × operation) pair at work order creation, then updated
/// automatically whenever the batch-level operation starts/completes — the shop-floor terminal still
/// operates at batch level (one button starts/completes a phase for the whole job), this table is purely
/// a derived history, not a new place operators interact with. A unit that gets scrapped stops receiving
/// updates from that point on (see WorkOrdersController), so its later phases simply stay whatever they
/// were at scrap time.</summary>
public class WorkOrderUnitOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderUnitId { get; set; }
    public WorkOrderUnit Unit { get; set; } = null!;
    public Guid WorkOrderOperationId { get; set; }
    public WorkOrderOperation Operation { get; set; } = null!;
    public string Status { get; set; } = "Pending";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
