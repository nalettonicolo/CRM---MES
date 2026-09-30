namespace CrmMes.Core.Models;

/// <summary>Sales invoice issued as an Italian electronic invoice (FatturaPA, FPR12). Usually a deferred
/// invoice (TD24) grouping the issued transport documents of a customer, or an immediate one (TD01).
///
/// Draft -&gt; Issued. A draft is edited freely and has no number; issuing assigns the progressive number of
/// the year and freezes it, because the XML sent to the Exchange System (SdI) must never change. The XML
/// is produced on demand from the frozen data. Mistakes on an issued invoice are corrected with a credit
/// note in the accounting system, not by editing.</summary>
public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? Number { get; set; }
    public int? Year { get; set; }

    /// <summary>"Draft" or "Issued".</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>FatturaPA TipoDocumento: "TD01" immediate invoice, "TD24" deferred invoice from DDTs.</summary>
    public string DocumentType { get; set; } = "TD01";

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime? IssueDate { get; set; }

    /// <summary>FatturaPA ModalitaPagamento, e.g. "MP05" bank transfer, "MP01" cash, "MP08" card.</summary>
    public string PaymentMethod { get; set; } = "MP05";
    public DateTime? PaymentDueDate { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? IssuedAt { get; set; }
    public string? IssuedBy { get; set; }

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
    public ICollection<InvoiceTransportDocument> TransportDocuments { get; set; } = new List<InvoiceTransportDocument>();
}

public class InvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public int LineNumber { get; set; }
    public string? Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "pz";
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }

    /// <summary>VAT rate in percent (22, 10, 5, 4, 0).</summary>
    public decimal VatRate { get; set; } = 22;

    /// <summary>FatturaPA Natura, required when the rate is 0 (e.g. "N3.1" exports, "N6.7" reverse charge in construction).</summary>
    public string? VatNature { get; set; }

    /// <summary>The DDT this line comes from, for the DatiDDT references of a deferred invoice.</summary>
    public Guid? TransportDocumentId { get; set; }
}

/// <summary>A DDT invoiced by an invoice: each issued DDT can be invoiced only once.</summary>
public class InvoiceTransportDocument
{
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public Guid TransportDocumentId { get; set; }
    public TransportDocument TransportDocument { get; set; } = null!;
}

/// <summary>Tax data of a customer for electronic invoices: the address in structured form (the SdI wants
/// street, postcode, town, province and country separately), the tax code and where to deliver
/// (7-character recipient code, or certified e-mail).</summary>
public class CustomerFiscalData
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string? FiscalCode { get; set; }
    public string? SdiCode { get; set; }
    public string? Pec { get; set; }
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string Country { get; set; } = "IT";
}
