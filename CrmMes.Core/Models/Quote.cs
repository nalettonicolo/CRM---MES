namespace CrmMes.Core.Models;

/// <summary>A customer quote (preventivo). Lifecycle: Draft -> Sent -> Accepted | Rejected, and an
/// Accepted quote can be converted once into work orders (one per product line), which stamps
/// <see cref="ConvertedAt"/>. Only a Draft can be edited.</summary>
public class Quote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string Status { get; set; } = "Draft";
    public DateTime? ValidUntil { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public DateTime? ConvertedAt { get; set; }
    public ICollection<QuoteItem> Items { get; set; } = new List<QuoteItem>();
}
