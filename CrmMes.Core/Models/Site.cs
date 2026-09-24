namespace CrmMes.Core.Models;

/// <summary>A physical location/plant of the same company — the "seconda sede" concept: not a separate
/// tenant with isolated data (users, materials, suppliers and products stay shared across the whole
/// company), just a dimension to filter and report Areas and WorkCenters by. Optional on both: an Area
/// or WorkCenter with no Site is simply not yet assigned to one, not an error.</summary>
public class Site
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
