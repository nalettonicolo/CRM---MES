namespace CrmMes.Core.Models;

/// <summary>A physical machine/asset that needs maintenance — the CMMS counterpart to WorkCenter
/// (a "reparto/linea" can host one or more machines). Optionally tied to a WorkCenter so a downtime
/// reported there can be linked to the specific machine that caused it.</summary>
public class Equipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public Guid? WorkCenterId { get; set; }
    public WorkCenter? WorkCenter { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
