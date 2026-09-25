namespace CrmMes.Core.Models;

/// <summary>One paintable phase category on the production planning board (e.g. "Progettazione
/// meccanica", "Montaggio") — user-defined, not a fixed enum, so the board can be reshaped to match
/// how this company actually organizes a build without a code change. Code is the short letter shown
/// inside a painted cell (e.g. "P"), Color is what fills/highlights it.</summary>
public class PlanningCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#8C7F6A";
    public int SequenceNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
