namespace CrmMes.Core.Models;

public class Material
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "pz";
    public decimal Stock { get; set; }
    public decimal MinStock { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MaterialSupplier> Suppliers { get; set; } = new List<MaterialSupplier>();
}
