namespace CrmMes.Core.Models;

/// <summary>Certificato di avanzamento lavori (SAL) registrato da import pacchetto settore.</summary>
public class ProgressCertificate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public decimal PercentComplete { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Lettura da bilance di produzione (integrazione pacchetto settore).</summary>
public class ScaleReading
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MaterialCode { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public string Unit { get; set; } = "kg";
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
