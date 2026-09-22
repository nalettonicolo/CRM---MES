namespace CrmMes.Core.Models;

/// <summary>A production job (commessa): build N units of a product, tracked through a snapshot of
/// its routing at release time.</summary>
public class WorkOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Quantity { get; set; }
    public Guid? AreaId { get; set; }
    public Area? Area { get; set; }
    public string? CustomerReference { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime? DueDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReleasedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ICollection<WorkOrderOperation> Operations { get; set; } = new List<WorkOrderOperation>();
}
