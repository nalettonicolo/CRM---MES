namespace CrmMes.Web.Services;

public sealed record PurchaseInvoiceSummary(
    Guid Id, string DocumentNumber, DateTime DocumentDate, string DocumentType,
    string SupplierName, string? SupplierVat, Guid? SupplierId, decimal TaxableTotal,
    DateTime ImportedAt, int OpenInstallments, int PaidInstallments);

public sealed record PurchaseInvoiceLine(
    int LineNumber, string? Code, string Description, decimal Quantity, string Unit,
    decimal UnitPrice, decimal DiscountPercent, decimal VatRate, string? VatNature);

public sealed record PaymentScheduleEntry(
    Guid Id, string Direction, string Status, DateTime DueDate, decimal Amount, string Currency,
    string? CounterpartyName, string? Description, Guid? PurchaseInvoiceId, Guid? InvoiceId,
    DateTime? PaidAt, DateTime? RemindedAt, int ReminderCount, bool Overdue);

public sealed record PurchaseInvoiceDetail(
    Guid Id, string DocumentNumber, DateTime DocumentDate, string DocumentType,
    string SupplierName, string? SupplierVat, Guid? SupplierId, string? PaymentMethod,
    string? Currency, string? OriginalFileName, DateTime ImportedAt, string? ImportedBy,
    List<PurchaseInvoiceLine> Lines, List<PaymentScheduleEntry> Schedule);

public static class PayablesRules
{
    public static bool CanImportPassive(string role) => role is "Admin" or "Purchasing";

    public static bool CanSeeSchedule(string role) => role is "Admin" or "Purchasing" or "Sales" or "Management";
}
