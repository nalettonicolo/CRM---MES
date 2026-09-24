namespace CrmMes.Core.Models;

public class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }

    /// <summary>Expected delivery date as agreed with the supplier — set by purchasing when confirming
    /// the order, independent of any shipment (a shipment may not exist yet). Feeds the planning
    /// calendar; purely informational, never enforced.</summary>
    public DateTime? ExpectedDeliveryDate { get; set; }
    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}
