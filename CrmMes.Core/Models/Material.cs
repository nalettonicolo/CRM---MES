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

    /// <summary>Food module: name of the ingredient on the label, when different from the stock name
    /// (e.g. "farina di grano tenero tipo 00" for "Farina 00 sacco 25 kg").</summary>
    public string? IngredientName { get; set; }

    /// <summary>Food module: allergens of Reg. (UE) 1169/2011 Annex II contained, comma-separated keys
    /// (see FoodAllergens in CrmMes.Api).</summary>
    public string? Allergens { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MaterialSupplier> Suppliers { get; set; } = new List<MaterialSupplier>();
}
