namespace CrmMes.Core.Models;

/// <summary>A technical document of a product: drawing, wiring diagram, work instructions, CNC program, photo.
/// Optionally tied to one step of the routing (StepSequence): the operators of that phase see it at the
/// terminal. A new version supersedes the previous one, which stays in the history.</summary>
public class TechnicalDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Routing step (by sequence number) the document belongs to; null for the whole product.</summary>
    public int? StepSequence { get; set; }

    /// <summary>"drawing", "schema", "instructions", "cnc", "photo", "other" (see TechnicalDocumentKinds).</summary>
    public string Kind { get; set; } = "drawing";
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    /// <summary>1, 2, 3... per document (same Title, product and step).</summary>
    public int Version { get; set; } = 1;
    public string? VersionNote { get; set; }
    public bool IsCurrent { get; set; } = true;
    public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? Sha256 { get; set; }
    public TechnicalDocumentContent? Content { get; set; }
}

/// <summary>The file itself, in its own table so listing documents never loads megabytes.</summary>
public class TechnicalDocumentContent
{
    public Guid TechnicalDocumentId { get; set; }
    public byte[] Data { get; set; } = [];
}

/// <summary>A released revision of a product: its bill of materials and routing as they were, kept when an
/// engineering change moves the product to the next revision.</summary>
public class ProductRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public string Revision { get; set; } = "A";
    public string BomJson { get; set; } = "[]";
    public string RoutingJson { get; set; } = "[]";
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
    public Guid? ReplacedByChangeId { get; set; }
}

/// <summary>An engineering change: a proposed new bill of materials and/or routing for a product, approved and
/// then applied. Applying archives the current revision, moves the product to the next one, and updates the
/// work orders still in draft (the others are listed for the shop floor to decide).</summary>
public class EngineeringChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Number { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Reason { get; set; }

    /// <summary>Proposed bill of materials / routing as JSON; null leaves that part unchanged.</summary>
    public string? NewBomJson { get; set; }
    public string? NewRoutingJson { get; set; }

    /// <summary>"draft", "approved", "applied", "rejected" (see EngineeringChangeStatus).</summary>
    public string Status { get; set; } = "draft";
    public string? FromRevision { get; set; }
    public string? ToRevision { get; set; }
    public string? RequestedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? AppliedBy { get; set; }

    /// <summary>What applying did to the open work orders, for the record.</summary>
    public string? ApplyReport { get; set; }
}

public static class EngineeringChangeStatus
{
    public const string Draft = "draft";
    public const string Approved = "approved";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
}

public static class TechnicalDocumentKinds
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["drawing"] = "Disegno",
        ["schema"] = "Schema elettrico",
        ["instructions"] = "Istruzioni di lavoro",
        ["cnc"] = "Programma CNC",
        ["photo"] = "Foto",
        ["other"] = "Altro",
    };
}
