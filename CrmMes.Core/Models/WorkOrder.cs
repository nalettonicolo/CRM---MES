namespace CrmMes.Core.Models;

/// <summary>A production job (commessa): build N units of a product, tracked through a snapshot of
/// its routing at release time.</summary>
public class WorkOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;

    /// <summary>Batch/lot identifier for the finished goods this work order produces — the traceability
    /// anchor a customer complaint or a recall would search by. All units from one work order share this
    /// one lot number; <see cref="Units"/> is the finer per-serial layer on top of it.</summary>
    public string ProductLotNumber { get; set; } = string.Empty;

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
    public ICollection<WorkOrderUnit> Units { get; set; } = new List<WorkOrderUnit>();
}
