namespace CrmMes.Core.Models;

/// <summary>Documento di trasporto (DDT, DPR 472/1996): accompanies goods leaving the company, for any
/// reason (causale) — a sale, goods sent to a subcontractor, a repair, a return to a supplier.
///
/// Draft -&gt; Issued, or Cancelled once issued. A draft can be edited freely and has no number: the
/// progressive number within the year is assigned only at issue, so abandoned drafts never leave gaps
/// in the sequence. An issued document is never edited or deleted (it has been printed and travelled
/// with the goods): a mistake is fixed by cancelling it, which keeps its number, and issuing a new one.
///
/// Sector-agnostic on purpose: recipient and destination are free text (prefilled from a customer or a
/// supplier) and lines may or may not point to a material or product of the catalog.</summary>
public class TransportDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Assigned at issue: progressive within <see cref="Year"/>, starting from 1.</summary>
    public int? Number { get; set; }
    public int? Year { get; set; }

    /// <summary>"Draft", "Issued" or "Cancelled".</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>Causale del trasporto: a key such as "Sale" or "Subcontracting" (see TransportReasons in
    /// CrmMes.Api); "Other" uses <see cref="ReasonDetail"/> as its text.</summary>
    public string Reason { get; set; } = "Sale";
    public string? ReasonDetail { get; set; }

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string RecipientName { get; set; } = string.Empty;
    public string? RecipientAddress { get; set; }
    public string? RecipientVatNumber { get; set; }

    /// <summary>Luogo di destinazione, when different from the recipient's address.</summary>
    public string? DestinationAddress { get; set; }

    /// <summary>Trasporto a cura di: "Sender" (mittente), "Recipient" (destinatario) or "Carrier" (vettore).</summary>
    public string TransportBy { get; set; } = "Sender";
    public Guid? CarrierId { get; set; }
    public Carrier? Carrier { get; set; }

    /// <summary>Porto: "Franco" (paid by the sender) or "Assegnato" (paid by the recipient).</summary>
    public string? Port { get; set; }
    public string? GoodsAppearance { get; set; }
    public int? Packages { get; set; }
    public decimal? GrossWeightKg { get; set; }
    public DateTime? TransportStartAt { get; set; }

    /// <summary>Goods sent to a subcontractor: when they are expected back (drives the overdue list).</summary>
    public DateTime? ExpectedReturnAt { get; set; }

    public Guid? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? IssuedAt { get; set; }
    public string? IssuedBy { get; set; }
    public DateTime? CancelledAt { get; set; }

    public ICollection<TransportDocumentLine> Lines { get; set; } = new List<TransportDocumentLine>();
}

/// <summary>One row of goods on a DDT. Code and description are copied at the time of writing, so the
/// document keeps saying what actually travelled even if the catalog item is renamed later.</summary>
public class TransportDocumentLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransportDocumentId { get; set; }
    public TransportDocument TransportDocument { get; set; } = null!;
    public int LineNumber { get; set; }

    public Guid? MaterialId { get; set; }
    public Material? Material { get; set; }
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    public string? Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "pz";
    public string? LotNumber { get; set; }
    public string? Notes { get; set; }

    /// <summary>Returns from the subcontractor (conto lavoro), only for "Subcontracting" documents.</summary>
    public ICollection<SubcontractingReturn> Returns { get; set; } = new List<SubcontractingReturn>();
}

/// <summary>Goods coming back from a subcontractor against a line sent in conto lavorazione, with the
/// subcontractor's own transport document as reference. A line can come back in several instalments;
/// what's still outstanding is the quantity sent minus all its returns.</summary>
public class SubcontractingReturn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransportDocumentLineId { get; set; }
    public TransportDocumentLine Line { get; set; } = null!;
    public decimal Quantity { get; set; }

    /// <summary>Quantity scrapped by the subcontractor: closes the balance without coming back.</summary>
    public decimal ScrapQuantity { get; set; }
    public DateTime ReturnedAt { get; set; } = DateTime.UtcNow;
    public string? SupplierDocumentReference { get; set; }
    public string? Notes { get; set; }
    public string? RecordedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
