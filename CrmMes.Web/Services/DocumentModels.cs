namespace CrmMes.Web.Services;

// Transport documents and invoices (mirrors of the records in CrmMes.Api).

public sealed record TransportDocumentSummary(
    Guid Id, string DocumentCode, int? Number, int? Year, string Status, string Reason, string ReasonLabel,
    string RecipientName, DateTime? IssuedAt, DateTime CreatedAt, int LineCount, Guid? WorkOrderId);

public sealed record SubcontractingReturn(
    Guid Id, decimal Quantity, decimal ScrapQuantity, DateTime ReturnedAt, string? SupplierDocumentReference, string? Notes, string? RecordedBy);

public sealed record TransportDocumentLine(
    Guid Id, int LineNumber, Guid? MaterialId, Guid? ProductId, string? Code, string Description, decimal Quantity,
    string Unit, string? LotNumber, string? Notes, decimal? ReturnedQuantity, decimal? ScrapQuantity,
    decimal? OutstandingQuantity, List<SubcontractingReturn> Returns);

public sealed record TransportDocument(
    Guid Id, string DocumentCode, int? Number, int? Year, string Status, string Reason, string? ReasonDetail,
    string ReasonLabel, Guid? CustomerId, Guid? SupplierId, string RecipientName, string? RecipientAddress,
    string? RecipientVatNumber, string? DestinationAddress, string TransportBy, Guid? CarrierId, string? CarrierName,
    string? Port, string? GoodsAppearance, int? Packages, decimal? GrossWeightKg, DateTime? TransportStartAt,
    DateTime? ExpectedReturnAt, Guid? WorkOrderId, string? WorkOrderCode, string? Notes, string? CancellationReason,
    DateTime CreatedAt, string? CreatedBy, DateTime? IssuedAt, string? IssuedBy, DateTime? CancelledAt,
    List<TransportDocumentLine> Lines);

public sealed record InvoiceSummary(
    Guid Id, string Code, string Status, string DocumentType, Guid CustomerId, string CustomerName, DateTime? IssueDate,
    decimal Total, DateTime CreatedAt);

public sealed record InvoiceLine(
    Guid Id, int LineNumber, string? Code, string Description, decimal Quantity, string Unit, decimal UnitPrice,
    decimal DiscountPercent, decimal VatRate, string? VatNature, decimal LineTotal, Guid? TransportDocumentId);

public sealed record InvoiceDocument(Guid Id, string DocumentCode, DateTime? IssuedAt);

public sealed record InvoiceVatLine(decimal Rate, string? Nature, decimal Taxable, decimal Tax);

public sealed record Invoice(
    Guid Id, string Code, int? Number, int? Year, string Status, string DocumentType, Guid CustomerId, string CustomerName,
    DateTime? IssueDate, string PaymentMethod, DateTime? PaymentDueDate, string? Notes, List<InvoiceLine> Lines,
    List<InvoiceDocument> TransportDocuments, List<InvoiceVatLine> VatSummary, decimal Total,
    List<string> Warnings, DateTime? IssuedAt, string? IssuedBy);

public sealed record DownloadedFile(string FileName, string ContentType, byte[] Content);
