namespace CrmMes.Core.Models;

/// <summary>A single routing phase as executed for one work order — a snapshot of the product's
/// RoutingStep at the time the work order was created, so later edits to the product's routing
/// template don't retroactively change a job already on the floor.</summary>
public class WorkOrderOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? WorkCenter { get; set; }
    public decimal EstimatedMinutes { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Name of the operator who started/completed this phase, as identified by PIN at the
    /// shop-floor terminal — a display snapshot, not authoritative: kept even if the user is later
    /// renamed/deactivated, and still set from the office client (which has no PIN identification) via
    /// free text. StartedByUserId/CompletedByUserId are the authoritative link when the action did come
    /// from a PIN-identified operator; both are null when it came from the office client.</summary>
    public string? StartedBy { get; set; }
    public string? CompletedBy { get; set; }
    public Guid? StartedByUserId { get; set; }
    public User? StartedByUser { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public User? CompletedByUser { get; set; }

    /// <summary>Day-level finite-capacity plan produced by <c>POST /api/work-orders/{id}/schedule</c> —
    /// which calendar day(s) this operation is expected to run on, given its work center's registered
    /// daily capacity and everything else already scheduled there. Null until a schedule run covers it;
    /// this is a day-granularity plan, not a minute-precise timeline.</summary>
    public DateTime? PlannedStartAt { get; set; }
    public DateTime? PlannedEndAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
