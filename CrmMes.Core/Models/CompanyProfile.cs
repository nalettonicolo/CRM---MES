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

    /// <summary>Everything the company does, comma-separated sector keys (a machine builder is mechanics,
    /// panels and service at once). <see cref="Sector"/> is the first of them, kept for older clients.</summary>
    public string Activities { get; set; } = string.Empty;

    /// <summary>Enabled module keys, comma-separated (see Sectors in CrmMes.Api). Stored as text on
    /// purpose: the set of modules changes with the product, the schema shouldn't.</summary>
    public string EnabledModules { get; set; } = string.Empty;

    /// <summary>GS1 company prefix (7–10 digits, assigned by GS1 Italy) used to build SSCC pallet codes,
    /// and the last serial reference handed out: SSCCs are never reused.</summary>
    public string? Gs1CompanyPrefix { get; set; }

    /// <summary>Tax data printed in the electronic invoices (CedentePrestatore): tax code, tax regime
    /// (RF01 ordinary), address in structured form, REA registration and the IBAN for bank transfers.</summary>
    public string? FiscalCode { get; set; }
    public string TaxRegime { get; set; } = "RF01";
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string Country { get; set; } = "IT";
    public string? ReaOffice { get; set; }
    public string? ReaNumber { get; set; }
    public string? Iban { get; set; }
    public long LastSsccSerial { get; set; }

    /// <summary>From where people may use the system (desktop program, web platform, technicians' phone
    /// page), per role and per area, as JSON (see AccessChannels in CrmMes.Api). Empty: everything
    /// everywhere.</summary>
    public string AccessChannels { get; set; } = string.Empty;

    /// <summary>Roles that must use two-factor authentication, comma-separated. Empty: optional for all.</summary>
    public string TwoFactorRoles { get; set; } = string.Empty;

    /// <summary>Roles the Admin has authorised to change the module layouts (Layout dei moduli), comma-separated.
    /// The Admin can always do it; this list adds other roles. Empty: Admin only.</summary>
    public string LayoutEditorRoles { get; set; } = string.Empty;

    /// <summary>Subscription from the vendor console (see LicenseState in CrmMes.Api): the last signed license
    /// received, the console's public key pinned at the first contact, and when it was last checked.</summary>
    public string? LicenseToken { get; set; }
    public string? LicensePublicKey { get; set; }
    public DateTime? LicenseCheckedAt { get; set; }

    /// <summary>Declaration for Transizione 5.0 / iperammortamento perizie: share of substantial software
    /// development attributable to EU/EEA (percent), places of development, and who signs for the producer.</summary>
    public decimal SoftwareEuDevelopmentPercent { get; set; } = 100m;
    public string SoftwareDevelopmentPlaces { get; set; } = "Italia";
    public string? SoftwareOriginSignatory { get; set; }
    public DateTime? SoftwareOriginUpdatedAt { get; set; }

    /// <summary>Company visual theme as JSON (colours, radius, density). Empty = Officina defaults.
    /// See UiTheme in CrmMes.Api.</summary>
    public string UiTheme { get; set; } = string.Empty;

    /// <summary>BCP 47 locale for UI and formats (default Italian).</summary>
    public string Locale { get; set; } = "it-IT";

    /// <summary>ISO 4217 currency for documents and amounts (default EUR).</summary>
    public string Currency { get; set; } = "EUR";

    public DateTime ConfiguredAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
