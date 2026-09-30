namespace CrmMes.Core.Models;

/// <summary>The company using the system, configured once by an Admin at first start: its registry data
/// (printed on transport documents and declarations) and the industry it works in, which decides which
/// modules are switched on. There is exactly one row (see CompanyProfileController).</summary>
public class CompanyProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CompanyName { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>Industry key (see Sectors in CrmMes.Api): drives the default modules.</summary>
    public string Sector { get; set; } = "generic";

    /// <summary>Enabled module keys, comma-separated (see Sectors in CrmMes.Api). Stored as text on
    /// purpose: the set of modules changes with the product, the schema shouldn't.</summary>
    public string EnabledModules { get; set; } = string.Empty;

    /// <summary>GS1 company prefix (7–10 digits, assigned by GS1 Italy) used to build SSCC pallet codes,
    /// and the last serial reference handed out: SSCCs are never reused.</summary>
    public string? Gs1CompanyPrefix { get; set; }
    public long LastSsccSerial { get; set; }

    public DateTime ConfiguredAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
