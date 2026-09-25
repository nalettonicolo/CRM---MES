namespace CrmMes.Core.Models;

/// <summary>One painted week for one project on the planning board — "this category is active in this
/// project during this week". Several can exist for the same (project, week) with different categories,
/// which is exactly what lets overlapping phases render side by side in the same cell; the unique index
/// is on (project, category, week), not (project, week), so the same category can't be painted twice
/// onto the same cell but different categories can coexist there.</summary>
public class PlanningCell
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanningProjectId { get; set; }
    public PlanningProject Project { get; set; } = null!;
    public Guid PlanningCategoryId { get; set; }
    public PlanningCategory Category { get; set; } = null!;

    /// <summary>Monday of the week this cell belongs to (UTC, time component always midnight) — the
    /// same week-bucketing convention already used by the Pianificazione aggregation board.</summary>
    public DateTime WeekStart { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
