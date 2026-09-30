namespace CrmMes.Web.Services;

public sealed record TechnicalDocument(Guid Id, Guid ProductId, int? StepSequence, string Kind, string KindName, string Title, string FileName,
    string ContentType, long SizeBytes, int Version, string? VersionNote, bool IsCurrent, string? UploadedBy, DateTime UploadedAt);

public sealed record ProductListItem(Guid Id, string Code, string Name, bool IsActive, int BomItemCount, int RoutingStepCount);

public sealed record ProductBomItem(Guid Id, string MaterialCode, decimal Quantity, string? Notes);

public sealed record ProductRoutingStep(Guid Id, int SequenceNumber, string Name, string? Description, string? WorkCenter, decimal EstimatedMinutes);

public sealed record ProductDetail(Guid Id, string Code, string Name, string? Description, bool IsActive,
    List<ProductBomItem> BillOfMaterial, List<ProductRoutingStep> RoutingSteps);

public sealed record BomLine(string MaterialCode, decimal Quantity, string? Notes);

public sealed record RoutingLine(string Name, string? Description, string? WorkCenter, decimal EstimatedMinutes);

public sealed record ArchivedRevision(Guid Id, string Revision, DateTime ArchivedAt, Guid? ReplacedByChangeId, List<BomLine> Bom, List<RoutingLine> Routing);

public sealed record ProductRevisions(string CurrentRevision, List<ArchivedRevision> Archived);

public sealed record EngineeringChangeSummary(Guid Id, int Number, Guid ProductId, string ProductCode, string ProductName, string Title, string Status,
    string? FromRevision, string? ToRevision, string? RequestedBy, DateTime CreatedAt, bool ChangesBom, bool ChangesRouting);

public sealed record AffectedWorkOrder(Guid Id, string Code, string Status, string? ProductRevision);

public sealed record EngineeringChange(Guid Id, int Number, Guid ProductId, string ProductCode, string ProductName, string ProductRevision,
    string Title, string? Description, string? Reason, string Status, string? FromRevision, string? ToRevision, string? RequestedBy, DateTime CreatedAt,
    string? ApprovedBy, DateTime? ApprovedAt, string? AppliedBy, DateTime? AppliedAt, string? ApplyReport,
    List<BomLine> CurrentBom, List<RoutingLine> CurrentRouting, List<BomLine>? ProposedBom, List<RoutingLine>? ProposedRouting,
    List<AffectedWorkOrder> AffectedWorkOrders);

public sealed record EngineeringChangeRequest(Guid ProductId, string? Title, string? Description, string? Reason, List<BomLine>? Bom, List<RoutingLine>? Routing);

public static class EngineeringLabels
{
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
    {
        ["drawing"] = "Disegno",
        ["schema"] = "Schema elettrico",
        ["instructions"] = "Istruzioni di lavoro",
        ["cnc"] = "Programma CNC",
        ["photo"] = "Foto",
        ["other"] = "Altro",
    };

    public static string Status(string status) => status switch
    {
        "draft" => "Bozza",
        "approved" => "Approvata",
        "applied" => "Applicata",
        "rejected" => "Respinta",
        _ => status,
    };

    public static string StatusClass(string status) => status switch
    {
        "approved" => "info",
        "applied" => "ok",
        "rejected" => "danger",
        _ => "",
    };

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };
}
