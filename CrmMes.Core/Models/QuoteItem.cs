namespace CrmMes.Core.Models;

/// <summary>One line of a quote. A line tied to a <see cref="Product"/> becomes a work order when the
/// quote is converted; a line without one is a free-text item (installation, transport, a one-off
/// service) that is priced on the quote but never produced.</summary>
public class QuoteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
}
