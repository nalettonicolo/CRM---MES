namespace CrmMes.Core.Models;

public class MaterialSupplier
{
    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string PartNumber { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal LeadTimeDays { get; set; }
    public decimal UnitPrice { get; set; }
}
