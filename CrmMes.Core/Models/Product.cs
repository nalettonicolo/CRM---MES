namespace CrmMes.Core.Models;

/// <summary>A buildable item (e.g. a panel model) with its own bill of materials and routing.</summary>
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Food module: legal name on the label ("denominazione di vendita"), shelf life in days from
    /// production (drives the use-by date printed on labels), storage conditions and net quantity.</summary>
    public string? SalesName { get; set; }
    public int? ShelfLifeDays { get; set; }
    public bool UseByDate { get; set; }
    public string? StorageConditions { get; set; }
    public string? NetQuantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<BillOfMaterialItem> BillOfMaterial { get; set; } = new List<BillOfMaterialItem>();
    public ICollection<RoutingStep> RoutingSteps { get; set; } = new List<RoutingStep>();
}
