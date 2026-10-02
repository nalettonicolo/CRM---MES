namespace CrmMes.Web.Services;

// Sales: customers and quotes (mirrors of the records in CrmMes.Api CustomersController/QuotesController).

public sealed record Customer(
    Guid Id, string Code, string Name, string? VatNumber, string? Email, string? Phone, string? Address, string? Notes, bool IsActive);

public sealed record CustomerQuote(Guid Id, string Code, string Status, DateTime CreatedAt, DateTime? ValidUntil, decimal Total);

public sealed record CustomerWorkOrder(
    Guid Id, string Code, string ProductCode, string ProductName, decimal Quantity, string Status, DateTime? DueDate);

public sealed record CustomerDetail(Customer Customer, List<CustomerQuote> Quotes, List<CustomerWorkOrder> WorkOrders);

public sealed record QuoteSummary(
    Guid Id, string Code, Guid CustomerId, string CustomerName, string Status,
    DateTime CreatedAt, DateTime? ValidUntil, DateTime? ConvertedAt, int ItemCount, decimal Total);

public sealed record QuoteItem(
    Guid Id, int SequenceNumber, Guid? ProductId, string? ProductCode, string? ProductName,
    string Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineTotal);

public sealed record Quote(
    Guid Id, string Code, Guid CustomerId, string CustomerName, string CustomerCode, string Status,
    DateTime? ValidUntil, string? Notes, DateTime CreatedAt, DateTime? SentAt, DateTime? AcceptedAt,
    DateTime? RejectedAt, DateTime? ConvertedAt, decimal Total, List<QuoteItem> Items,
    List<ConvertedWorkOrder>? WorkOrders = null);

public sealed record ConvertedWorkOrder(Guid Id, string Code, string ProductCode, decimal Quantity);

public sealed record ConvertQuoteResult(Guid QuoteId, string QuoteCode, List<ConvertedWorkOrder> WorkOrders);

/// <summary>What a role may do on a quote, mirroring the API's policies ("Sales" for the commercial
/// steps, "SalesOrWarehouse" for turning an accepted quote into work orders). The API decides anyway.</summary>
public static class QuoteRules
{
    public static bool CanSell(string role) => role is "Admin" or "Sales";

    public static bool CanConvert(string role) => role is "Admin" or "Sales" or "Warehouse";

    public static bool CanSend(Quote quote, string role) => CanSell(role) && quote.Status == "Draft";

    public static bool CanDecide(Quote quote, string role) => CanSell(role) && quote.Status is "Draft" or "Sent";

    public static bool CanTurnIntoWorkOrders(Quote quote, string role) =>
        CanConvert(role) && quote.Status == "Accepted" && quote.ConvertedAt is null && quote.Items.Any(item => item.ProductId is not null);

    public static bool IsExpired(Quote quote, DateTime now) =>
        quote.ValidUntil is not null && quote.Status is "Draft" or "Sent" && quote.ValidUntil.Value.ToLocalTime().Date < now.ToLocalTime().Date;
}
