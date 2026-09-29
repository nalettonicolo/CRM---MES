using CrmMes.Core.Models;

namespace CrmMes.Api.Services;

/// <summary>The one place quote amounts are computed, so the quote screen, the customer detail and
/// the PDF can never disagree on a total. Amounts are rounded to the cent per line, like an invoice.</summary>
public static class QuotePricing
{
    public static decimal LineTotal(QuoteItem item) =>
        Math.Round(item.Quantity * item.UnitPrice * (100 - item.DiscountPercent) / 100, 2, MidpointRounding.AwayFromZero);

    public static decimal Total(IEnumerable<QuoteItem> items) => items.Sum(LineTotal);
}
