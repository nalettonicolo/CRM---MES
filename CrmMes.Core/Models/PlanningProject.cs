namespace CrmMes.Core.Models;

/// <summary>One row on the production planning board — a machine/build being tracked at a coarse,
/// week-by-week visual level for management/sales coordination, separate from the operational
/// phase-by-phase tracking a Commessa (WorkOrder) already does. Deliberately not the same entity as
/// WorkOrder: a project can exist here while still "In valutazione" (being quoted, no confirmed order
/// yet), which has no equivalent WorkOrder status. Optionally linked to a WorkOrder once/if the work
/// becomes a real internal job, but that link is never required.</summary>
public class PlanningProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>"Confermata", "InValutazione", "InProduzione", "Sospesa" or "Consegnata".</summary>
    public string Status { get; set; } = "InValutazione";

    public string? Notes { get; set; }
    public int SequenceNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<PlanningCell> Cells { get; set; } = new List<PlanningCell>();
}
