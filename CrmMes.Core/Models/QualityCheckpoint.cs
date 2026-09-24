namespace CrmMes.Core.Models;

/// <summary>A measurable characteristic to check on a product (a control plan entry) — e.g. "Diametro
/// foro" with a nominal value and tolerance band. Sector-agnostic on purpose, same pattern as
/// WorkCenter/OperationDowntime: free text for the name, no forced taxonomy of characteristic types.</summary>
public class QualityCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal? NominalValue { get; set; }

    /// <summary>Either bound can be left unset for a one-sided tolerance (e.g. "at least X", no upper
    /// limit). Both unset means the checkpoint is recorded for traceability but never flagged out of
    /// tolerance — a deliberate escape hatch for a purely observational measurement.</summary>
    public decimal? LowerLimit { get; set; }
    public decimal? UpperLimit { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
