namespace CrmMes.Core.Models;

/// <summary>A machine acceptance test of a work order: FAT (in the factory, before shipping) or SAT (at the
/// customer's site, after installation). A checklist of verifications with expected and measured values and an
/// outcome each. Draft while it's being filled in; closing it freezes it as Passed (nothing failed) or Failed
/// (at least one verification failed: fix the machine and run a new test). Only an Admin can reopen it.
///
/// Only for the "machine-testing" module (machine builders): the tests are part of the technical file and a
/// passed one is needed before the EU declaration of conformity can be issued.</summary>
public class MachineTest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>1, 2, 3... across the company ("COL 12").</summary>
    public int Number { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;

    /// <summary>"FAT" or "SAT".</summary>
    public string Kind { get; set; } = "FAT";

    /// <summary>"Draft", "Passed" or "Failed".</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>Serial number of the machine under test (matricola).</summary>
    public string? SerialNumber { get; set; }
    public string? Location { get; set; }
    public DateTime? TestDate { get; set; }

    /// <summary>Who attended for the customer (FAT witnessed by the customer, SAT acceptance).</summary>
    public string? CustomerWitness { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public string? TestedBy { get; set; }

    public ICollection<MachineTestItem> Items { get; set; } = new List<MachineTestItem>();
}

/// <summary>One verification of a machine test, e.g. "Continuità del circuito di protezione (EN 60204-1, 18.2)".</summary>
public class MachineTestItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineTestId { get; set; }
    public MachineTest MachineTest { get; set; } = null!;
    public int Sequence { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Expected { get; set; }
    public string? Measured { get; set; }

    /// <summary>null (not done yet), "Pass", "Fail" or "NotApplicable".</summary>
    public string? Result { get; set; }
    public string? Notes { get; set; }
}

/// <summary>One element of the technical file of a machine (Annex VII of Directive 2006/42/EC, Annex IV of
/// Regulation (EU) 2023/1230): where the document is kept and whether it is there. The file itself stays where
/// the company keeps it (archive, engineering documents): this is the checklist that says it is complete.</summary>
public class MachineTechnicalFileItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;

    /// <summary>Stable key of the element (see MachineTechnicalFile.Elements), or "custom-..." for added ones.</summary>
    public string Code { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional elements may be marked not applicable (e.g. no partly completed machinery inside).</summary>
    public bool Optional { get; set; }

    /// <summary>null (missing), "Present" or "NotApplicable".</summary>
    public string? Status { get; set; }

    /// <summary>Where it is: document code, archive folder, file name.</summary>
    public string? Reference { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>EU declaration of conformity of one machine. Draft while it's being filled in; issuing it freezes
/// it and records the legal basis in force on that day: Directive 2006/42/EC until 19 January 2027, Regulation
/// (EU) 2023/1230 from 20 January 2027. Issuing needs a passed acceptance test and a complete technical file.
/// Only an Admin can withdraw an issued declaration (back to draft, recorded in the audit log).</summary>
public class MachineDeclaration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;

    /// <summary>1, 2, 3... assigned when issued.</summary>
    public int? Number { get; set; }

    /// <summary>"Draft" or "Issued".</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>"2006/42/CE" or "2023/1230", set when issued.</summary>
    public string? LegalBasis { get; set; }

    public string MachineName { get; set; } = string.Empty;
    public string? Function { get; set; }
    public string? Model { get; set; }
    public string? Type { get; set; }
    public string? SerialNumber { get; set; }
    public int? YearOfConstruction { get; set; }

    /// <summary>Other EU legislation the machine complies with (e.g. "Direttiva 2014/30/UE (compatibilità elettromagnetica)").</summary>
    public string? OtherLegislation { get; set; }

    /// <summary>Harmonised standards and other technical specifications applied, one per line.</summary>
    public string? Standards { get; set; }

    /// <summary>Notified body and certificate, only for machines that need third-party assessment.</summary>
    public string? NotifiedBody { get; set; }

    /// <summary>Name and address of the person authorised to compile the technical file.</summary>
    public string? TechnicalFileKeeper { get; set; }
    public string? Place { get; set; }
    public string? SignatoryName { get; set; }
    public string? SignatoryRole { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? IssuedAt { get; set; }
    public string? IssuedBy { get; set; }
}
