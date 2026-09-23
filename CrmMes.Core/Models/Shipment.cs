namespace CrmMes.Core.Models;

/// <summary>A single shipment moved by a Carrier, either Inbound (goods arriving, typically from a
/// supplier) or Outbound (goods leaving, typically finished units from a work order) — direction is
/// fixed at creation. Optionally linked to the purchase order it's fulfilling (inbound) or the work
/// order it's delivering (outbound), but neither link is required: a shipment can stand alone, e.g. for
/// goods not otherwise tracked in the system. The counterpart — who it's coming from, or who it's going
/// to — is free text on purpose, the same sector-agnostic pattern as WorkOrder.CustomerReference: no
/// separate customer/supplier-contact module to maintain here.</summary>
public class Shipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;

    /// <summary>"Inbound" or "Outbound" — fixed at creation, never changes.</summary>
    public string Direction { get; set; } = string.Empty;

    public Guid CarrierId { get; set; }
    public Carrier Carrier { get; set; } = null!;
    public string? TrackingNumber { get; set; }

    /// <summary>Preparing -&gt; Shipped -&gt; Delivered, or Cancelled from either of the first two.</summary>
    public string Status { get; set; } = "Preparing";

    /// <summary>What this shipment is fulfilling — at most one of the two, and only meaningful for the
    /// matching direction (a purchase order for Inbound, a work order for Outbound). Purely informational:
    /// creating or advancing a shipment never changes the linked order/job's own status.</summary>
    public Guid? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Guid? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public string? CounterpartReference { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }

    public DateTime? ExpectedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
