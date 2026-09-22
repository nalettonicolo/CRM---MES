namespace CrmMes.Core.Models;

/// <summary>One material line of a product's bill of materials (distinta base).</summary>
public class BillOfMaterialItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string MaterialCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
