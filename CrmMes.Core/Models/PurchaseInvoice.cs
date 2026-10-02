namespace CrmMes.Core.Models;

/// <summary>Supplier (passive) electronic invoice imported from a FatturaPA XML the company received.
/// Kept separate from sales <see cref="Invoice"/>: different lifecycle (no SdI issue step), matched to a
/// supplier by VAT, and feeds the payment schedule (scadenziario) of amounts to pay.</summary>
public class PurchaseInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Supplier document number as written on the XML (Numero).</summary>
    public string DocumentNumber { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }

    /// <summary>FatturaPA TipoDocumento (TD01, TD24, …).</summary>
    public string DocumentType { get; set; } = "TD01";

    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierVat { get; set; }

    public string? PaymentMethod { get; set; }
    public string? Currency { get; set; } = "EUR";
    public string? Notes { get; set; }

    /// <summary>SHA-256 of the imported XML body, to refuse the same file twice.</summary>
    public string ContentHash { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public string? ImportedBy { get; set; }

    public ICollection<PurchaseInvoiceLine> Lines { get; set; } = new List<PurchaseInvoiceLine>();
    public ICollection<PaymentScheduleEntry> Schedule { get; set; } = new List<PaymentScheduleEntry>();
}

public class PurchaseInvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseInvoiceId { get; set; }
    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;
    public int LineNumber { get; set; }
    public string? Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "pz";
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal VatRate { get; set; } = 22;
    public string? VatNature { get; set; }
}

/// <summary>One due amount in the company payment schedule: money to pay (Payable, from a purchase
/// invoice) or to collect (Receivable, from an issued sales invoice). Reminders are recorded here
/// without sending email yet — the gate needs the action and history; delivery channels come later.</summary>
public class PaymentScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>"Payable" or "Receivable".</summary>
    public string Direction { get; set; } = "Payable";

    /// <summary>"Open", "Paid".</summary>
    public string Status { get; set; } = "Open";

    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? CounterpartyName { get; set; }
    public string? Description { get; set; }

    public Guid? PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public DateTime? PaidAt { get; set; }
    public string? PaidBy { get; set; }

    public DateTime? RemindedAt { get; set; }
    public int ReminderCount { get; set; }
    public string? ReminderNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
