namespace CrmMes.Core.Models;

public class WithdrawalSlip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public Guid AreaId { get; set; }
    public Area Area { get; set; } = null!;
    public Guid RequestedByUserId { get; set; }
    public User RequestedByUser { get; set; } = null!;
    /// <summary>Optional link to the production job this pick list was generated for/from.</summary>
    public Guid? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<WithdrawalItem> Items { get; set; } = new List<WithdrawalItem>();
}
