namespace CrmMes.Core.Models;

/// <summary>A buildable item (e.g. a panel model) with its own bill of materials and routing.</summary>
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BillOfMaterialItem> BillOfMaterial { get; set; } = new List<BillOfMaterialItem>();
    public ICollection<RoutingStep> RoutingSteps { get; set; } = new List<RoutingStep>();
}
