namespace CrmMes.Web.Services;

public sealed record MachineTestingSummary(Guid WorkOrderId, string WorkOrderCode, string WorkOrderStatus, string ProductCode, string ProductName,
    string? CustomerName, int Tests, string? LastTestKind, string? LastTestStatus, int FileDone, int FileTotal, string? DeclarationStatus, int? DeclarationNumber);

public sealed record MachineTestItem(int Sequence, string Section, string Description, string? Expected, string? Measured, string? Result, string? Notes);

public sealed record MachineTest(Guid Id, int Number, string Kind, string Status, string? SerialNumber, string? Location, DateTime? TestDate,
    string? CustomerWitness, string? Notes, string? CreatedBy, DateTime CreatedAt, DateTime? ClosedAt, string? TestedBy, List<MachineTestItem> Items);

public sealed record TechnicalFileItem(string Code, string Description, bool Optional, string? Status, string? Reference, string? UpdatedBy, DateTime? UpdatedAt);

public sealed record Manufacturer(string? Name, string? Address, string? VatNumber);

public sealed record MachineDeclaration(bool IsSaved, string Status, int? Number, string LegalBasis, string LegalBasisText,
    string MachineName, string? Function, string? Model, string? Type, string? SerialNumber, int? YearOfConstruction,
    string? OtherLegislation, string? Standards, string? NotifiedBody, string? TechnicalFileKeeper, string? Place,
    string? SignatoryName, string? SignatoryRole, string? Notes, DateTime? IssuedAt, string? IssuedBy);

public sealed record MachineDossier(Guid WorkOrderId, string WorkOrderCode, string WorkOrderStatus, string ProductCode, string ProductName,
    string? ProductRevision, string? CustomerName, Manufacturer Manufacturer, List<MachineTest> Tests, List<TechnicalFileItem> TechnicalFile,
    bool TechnicalFileComplete, MachineDeclaration Declaration, List<string> MissingForDeclaration);

/// <summary>Editable copy of a test row (records are immutable, the form needs fields).</summary>
public sealed class MachineTestItemForm
{
    public string Section { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Expected { get; set; }
    public string? Measured { get; set; }
    public string? Result { get; set; }
    public string? Notes { get; set; }
}

public sealed class TechnicalFileItemForm
{
    public string? Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Optional { get; set; }
    public string? Status { get; set; }
    public string? Reference { get; set; }
}

public sealed class MachineDeclarationForm
{
    public string MachineName { get; set; } = string.Empty;
    public string? Function { get; set; }
    public string? Model { get; set; }
    public string? Type { get; set; }
    public string? SerialNumber { get; set; }
    public int? YearOfConstruction { get; set; }
    public string? OtherLegislation { get; set; }
    public string? Standards { get; set; }
    public string? NotifiedBody { get; set; }
    public string? TechnicalFileKeeper { get; set; }
    public string? Place { get; set; }
    public string? SignatoryName { get; set; }
    public string? SignatoryRole { get; set; }
    public string? Notes { get; set; }
}

public static class MachineTestingLabels
{
    public static string TestStatus(string? status) => status switch
    {
        "Draft" => "In corso",
        "Passed" => "Superato",
        "Failed" => "Non superato",
        null => "Nessuno",
        _ => status,
    };

    public static string TestStatusClass(string? status) => status switch
    {
        "Passed" => "ok",
        "Failed" => "danger",
        "Draft" => "info",
        _ => "",
    };

    public static string Result(string? result) => result switch
    {
        "Pass" => "Superata",
        "Fail" => "Non superata",
        "NotApplicable" => "Non applicabile",
        _ => "Da fare",
    };

    public static string ResultClass(string? result) => result switch
    {
        "Pass" => "ok",
        "Fail" => "danger",
        "NotApplicable" => "",
        _ => "warn",
    };

    public static string Declaration(string? status, int? number) => status switch
    {
        "Issued" => $"Emessa n. {number}",
        "Draft" => "In bozza",
        _ => "Da compilare",
    };

    public static string DeclarationClass(string? status) => status switch
    {
        "Issued" => "ok",
        "Draft" => "info",
        _ => "",
    };

    public static string Kind(string kind) => kind == "SAT" ? "SAT · presso il cliente" : "FAT · in fabbrica";
}
