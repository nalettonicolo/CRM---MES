namespace CrmMes.Core.Models;

/// <summary>Verifica individuale (routine verification) of a low-voltage switchgear assembly per
/// CEI EN 61439-1 clause 11, done by the assembly manufacturer (the panel builder) on every panel before
/// it leaves, plus the rated data printed on the declaration of conformity and on the nameplate.
///
/// Only for the "panel-verification" module (electrical panel builders): other sectors use the generic
/// quality plan and certificate. One per work order. Draft while it's being filled in; Completed freezes
/// it (only an Admin can reopen it), and only a completed verification with every check passed or not
/// applicable can be printed as a declaration.</summary>
public class PanelVerification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;

    /// <summary>"Draft" or "Completed".</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>Product standard of the assembly, e.g. "CEI EN 61439-2" (power switchgear, PSC) or
    /// "CEI EN 61439-3" (distribution boards for ordinary persons, DBO).</summary>
    public string Standard { get; set; } = "CEI EN 61439-2";

    /// <summary>Original manufacturer of the design-verified system (enclosure/busbar system), when the
    /// panel builder assembles a system designed by someone else.</summary>
    public string? OriginalManufacturer { get; set; }
    public string? SystemReference { get; set; }
    public string? SerialNumber { get; set; }

    public decimal? RatedVoltage { get; set; }          // Un, V
    public decimal? RatedCurrent { get; set; }          // InA, A
    public decimal? RatedFrequency { get; set; }        // fn, Hz
    public decimal? ShortTimeWithstandCurrent { get; set; } // Icw, kA
    public decimal? ConditionalShortCircuitCurrent { get; set; } // Icc, kA
    public string? IpRating { get; set; }
    public string? InternalSeparation { get; set; }     // forma di segregazione
    public string? EarthingSystem { get; set; }         // TN-S, TT...
    public decimal? InsulationResistanceMOhm { get; set; }
    public decimal? DielectricTestVoltage { get; set; } // V

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? VerifiedBy { get; set; }

    public ICollection<PanelVerificationCheck> Checks { get; set; } = new List<PanelVerificationCheck>();
}

/// <summary>One routine verification item (e.g. clause 11.9 "Proprietà dielettriche") and its outcome.</summary>
public class PanelVerificationCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PanelVerificationId { get; set; }
    public PanelVerification PanelVerification { get; set; } = null!;
    public string Clause { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>null (not done yet), "Pass", "Fail" or "NotApplicable".</summary>
    public string? Result { get; set; }
    public string? Notes { get; set; }
}
