namespace CrmMes.Core.Models;

/// <summary>A traceable batch of a material: one intake (a purchase order receipt, a manual stock
/// correction, or the opening stock declared when the material was created). Quantity is what remains
/// in this lot after withdrawals have consumed from it; InitialQuantity is the original intake amount,
/// kept for audit even once the lot is fully consumed.</summary>
public class MaterialLot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MaterialCode { get; set; } = string.Empty;
    public string LotNumber { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal InitialQuantity { get; set; }
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MaterialLotConsumption> Consumptions { get; set; } = new List<MaterialLotConsumption>();
}
