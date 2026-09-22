namespace CrmMes.Core.Models;

/// <summary>One phase of a product's work cycle (ciclo di lavoro), e.g. cutting, assembly, testing —
/// deliberately sector-agnostic: <see cref="Name"/> and <see cref="WorkCenter"/> are free text so any
/// manufacturing sector can use its own terminology.</summary>
public class RoutingStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? WorkCenter { get; set; }
    public decimal EstimatedMinutes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
