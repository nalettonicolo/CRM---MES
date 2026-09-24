namespace CrmMes.Core.Models;

/// <summary>Master data record for a work center (reparto/linea/postazione) so its daily capacity can
/// be registered and compared against the pending workload. Routing steps and work order operations
/// keep referencing work centers by free-text name (sector-agnostic, no forced setup); this table is an
/// optional catalog that the load overview matches against that free text by name.</summary>
public class WorkCenter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Available production minutes per day (e.g. one shift = 480). Used only to give an
    /// indicative backlog-in-days figure — not a calendar-based finite-capacity schedule.</summary>
    public decimal DailyCapacityMinutes { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Which physical site (seconda sede) this work center is at. Null = not yet assigned.</summary>
    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }
}
