namespace CrmMes.Core.Models;

/// <summary>A pallet or logistic unit identified by an SSCC (Serial Shipping Container Code, GS1): the
/// 18-digit code printed as a GS1-128 barcode on the pallet label, unique worldwide because it starts
/// with the company's GS1 prefix. Allocated one at a time from the company profile's counter, never
/// reused, and tied to what the pallet carries (work order / product lot, and optionally the DDT).</summary>
public class LogisticUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sscc { get; set; } = string.Empty;
    public Guid? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public Guid? TransportDocumentId { get; set; }
    public TransportDocument? TransportDocument { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public string? LotNumber { get; set; }
    public decimal? Quantity { get; set; }
    public DateTime? BestBefore { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Critical control point of the HACCP plan (e.g. "Cella frigo 1, temperatura, 0–4 °C, due volte
/// al giorno"). A numeric point has limits; a yes/no point (e.g. "Pulizia affettatrice eseguita") has
/// none and its readings are just compliant or not.</summary>
public class HaccpControlPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Hazard { get; set; }
    public string? Unit { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Frequency { get; set; }
    public string? CorrectiveActionHint { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<HaccpReading> Readings { get; set; } = new List<HaccpReading>();
}

/// <summary>One monitoring record of a control point. Out of limits (or not compliant) it must carry the
/// corrective action taken: that is what the HACCP register has to show an inspector.</summary>
public class HaccpReading
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ControlPointId { get; set; }
    public HaccpControlPoint ControlPoint { get; set; } = null!;
    public decimal? Value { get; set; }
    public bool Compliant { get; set; }
    public string? CorrectiveAction { get; set; }
    public string? Notes { get; set; }
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
    public string? OperatorName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
