namespace CrmMes.Core.Models;

public class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>Riferimento rapido al catalogo online del fornitore, per quando non esiste un PDF/Excel
    /// da importare (il catalogo è il sito stesso). Puramente informativo: nessuna validazione oltre
    /// alla lunghezza, l'utente incolla il link che usa già.</summary>
    public string? Website { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MaterialSupplier> Materials { get; set; } = new List<MaterialSupplier>();
}
