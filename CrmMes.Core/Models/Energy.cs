namespace CrmMes.Core.Models;

/// <summary>An energy efficiency project on one machine: a "before" period measured as a baseline and an
/// "after" period measured once the improvement (a new machine, an inverter, a heat recovery...) is running.
/// Built for the ex-ante/ex-post report that an energy-efficiency tax relief (e.g. the 2026 hyper-depreciation,
/// Industria 5.0) asks for — the consumption figures themselves come from the machine's own energy readings
/// (MachineEvent.EnergyKwh), never typed in by hand, so the report stays auditable.</summary>
public class EnergyProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DateTime BaselineFrom { get; set; }
    public DateTime BaselineTo { get; set; }

    /// <summary>Set once the "after" window is defined (typically once the improvement has been running long
    /// enough to measure); null while still in the baseline/monitoring stage.</summary>
    public DateTime? AfterFrom { get; set; }
    public DateTime? AfterTo { get; set; }

    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
