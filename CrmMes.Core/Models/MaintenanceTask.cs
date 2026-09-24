namespace CrmMes.Core.Models;

/// <summary>A maintenance intervention on a piece of equipment — either "Preventiva" (scheduled ahead
/// of time, with an optional recurrence so completing it can spawn the next occurrence) or "Correttiva"
/// (ad-hoc, typically opened from a machine breakdown/downtime). This is the piece that lets the
/// dashboard's Availability figure eventually be explained/acted on, not just measured.</summary>
public class MaintenanceTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"Preventiva" or "Correttiva".</summary>
    public string Type { get; set; } = "Preventiva";

    /// <summary>"Pending" or "Completed" — no separate "InProgress": a maintenance task here is a single
    /// point-in-time record of work to do/done, not itself tracked minute-by-minute like a work order
    /// operation.</summary>
    public string Status { get; set; } = "Pending";

    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public User? CompletedByUser { get; set; }
    public string? Notes { get; set; }

    /// <summary>When set (and only meaningful for a Preventiva task), completing this task creates the
    /// next occurrence automatically, due this many days later — so a maintenance plan self-perpetuates
    /// instead of needing to be manually rescheduled every time.</summary>
    public int? RecurrenceDays { get; set; }

    /// <summary>Optional link to the downtime that prompted this task, when it's corrective maintenance
    /// opened in response to a machine stop reported on the shop floor.</summary>
    public Guid? OperationDowntimeId { get; set; }
    public OperationDowntime? OperationDowntime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
