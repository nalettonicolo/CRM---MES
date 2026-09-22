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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
