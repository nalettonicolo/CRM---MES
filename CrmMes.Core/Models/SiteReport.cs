namespace CrmMes.Core.Models;

/// <summary>Rapportino di intervento: the work done at the customer's site on one day for a work order
/// (installers, maintenance technicians): description, hours per technician, materials installed, and the
/// customer's signature. Draft while being filled in; signing freezes it, turns its hours into labor
/// entries of the work order (so they count in the job cost) and its materials count in the actual
/// material cost. A signed report is never edited: a mistake is fixed with a new report.</summary>
public class SiteReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;

    /// <summary>"Draft" or "Signed".</summary>
    public string Status { get; set; } = "Draft";
    public DateTime WorkDate { get; set; }
    public string? SiteAddress { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public string? SignedByName { get; set; }

    /// <summary>The customer's handwritten signature as a PNG data URL, drawn on a touch screen.</summary>
    public string? SignatureImage { get; set; }
    public DateTime? SignedAt { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SiteReportHours> Hours { get; set; } = new List<SiteReportHours>();
    public ICollection<SiteReportMaterial> Materials { get; set; } = new List<SiteReportMaterial>();
}

public class SiteReportHours
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteReportId { get; set; }
    public SiteReport SiteReport { get; set; } = null!;
    public string TechnicianName { get; set; } = string.Empty;
    public Guid? WorkCenterId { get; set; }
    public WorkCenter? WorkCenter { get; set; }
    public decimal Minutes { get; set; }
}

public class SiteReportMaterial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteReportId { get; set; }
    public SiteReport SiteReport { get; set; } = null!;
    public string? MaterialCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "pz";
}
