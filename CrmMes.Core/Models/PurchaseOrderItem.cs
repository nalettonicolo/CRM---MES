namespace CrmMes.Core.Models;

public class PurchaseOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public string MaterialCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public Guid? MissingMaterialId { get; set; }
    public MissingMaterial? MissingMaterial { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
