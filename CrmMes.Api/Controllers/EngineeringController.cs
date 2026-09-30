using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Engineering office: technical documents of a product (drawings, wiring diagrams, work instructions,
/// CNC programs) with versions, product revisions (A, B, C...) and engineering changes — proposed, approved and
/// applied. Applying a change archives the current bill of materials and routing, moves the product to the next
/// revision and rebuilds the work orders still in draft; released jobs are listed, never touched.</summary>
[ApiController]
[Authorize]
[Route("api/engineering")]
public class EngineeringController(ApplicationDbContext db) : ControllerBase
{
    public const long MaxDocumentBytes = 20 * 1024 * 1024;

    /// <summary>Files that would run instead of opening: refused, because terminals open documents with the
    /// default program of the PC.</summary>
    public static readonly IReadOnlySet<string> BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".msi", ".msp", ".scr", ".pif", ".lnk", ".url", ".hta", ".cpl", ".dll", ".sys",
        ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".jar", ".reg", ".appref-ms", ".application", ".msc",
        ".html", ".htm", ".svg", ".xhtml",
    };

    public static bool IsBlocked(string fileName) => BlockedExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>The type is decided here from the extension, never taken from the uploader: only PDFs and images
    /// may be shown inline by a browser; everything else is a plain download.</summary>
    public static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        _ => "application/octet-stream",
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Technical documents ----------

    [HttpGet("products/{productId:guid}/documents")]
    public async Task<ActionResult<IEnumerable<TechnicalDocumentResponse>>> GetDocuments(Guid productId, [FromQuery] bool history = false, CancellationToken cancellationToken = default)
    {
        var query = db.TechnicalDocuments.AsNoTracking().Where(d => d.ProductId == productId);
        if (!history)
        {
            query = query.Where(d => d.IsCurrent);
        }

        var documents = await query.ToListAsync(cancellationToken);
        return Ok(documents
            .OrderBy(d => d.StepSequence ?? 0).ThenBy(d => d.Title).ThenByDescending(d => d.Version)
            .Select(ToResponse));
    }

    /// <summary>Uploads a document. Same title and phase as a current document: it becomes its next version and the
    /// previous one goes to the history.</summary>
    [Authorize(Policy = "Engineering")]
    [HttpPost("products/{productId:guid}/documents")]
    [RequestSizeLimit(MaxDocumentBytes + 1_000_000)]
    public async Task<ActionResult<TechnicalDocumentResponse>> Upload(
        Guid productId,
        IFormFile? file,
        [FromForm] string? title,
        [FromForm] string? kind,
        [FromForm] int? stepSequence,
        [FromForm] string? versionNote,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Scegli il file da caricare." });
        }

        if (file.Length > MaxDocumentBytes)
        {
            return BadRequest(new { message = $"Il file supera {MaxDocumentBytes / 1024 / 1024} MB." });
        }

        kind = string.IsNullOrWhiteSpace(kind) ? "drawing" : kind.Trim();
        if (!TechnicalDocumentKinds.All.ContainsKey(kind))
        {
            return BadRequest(new { message = "Tipo di documento non valido." });
        }

        var product = await db.Products.Include(p => p.RoutingSteps).SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (stepSequence is not null && product.RoutingSteps.All(s => s.SequenceNumber != stepSequence))
        {
            return BadRequest(new { message = "La fase indicata non esiste nel ciclo del prodotto." });
        }

        var fileName = Path.GetFileName(file.FileName);
        if (IsBlocked(fileName))
        {
            return BadRequest(new { message = "Questo tipo di file non si può caricare: usa PDF, immagini, documenti o file CNC." });
        }

        title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim();
        if (title.Length > 200)
        {
            return BadRequest(new { message = "Titolo troppo lungo (massimo 200 caratteri)." });
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        var data = stream.ToArray();

        var previous = await db.TechnicalDocuments
            .Where(d => d.ProductId == productId && d.IsCurrent && d.Title == title && d.StepSequence == stepSequence)
            .ToListAsync(cancellationToken);
        foreach (var old in previous)
        {
            old.IsCurrent = false;
        }

        var lastVersion = await db.TechnicalDocuments
            .Where(d => d.ProductId == productId && d.Title == title && d.StepSequence == stepSequence)
            .MaxAsync(d => (int?)d.Version, cancellationToken) ?? 0;

        var document = new TechnicalDocument
        {
            ProductId = productId,
            StepSequence = stepSequence,
            Kind = kind,
            Title = title,
            FileName = fileName.Length > 260 ? fileName[^260..] : fileName,
            ContentType = ContentTypeFor(fileName),
            SizeBytes = data.LongLength,
            Version = lastVersion + 1,
            VersionNote = string.IsNullOrWhiteSpace(versionNote) ? null : versionNote.Trim(),
            UploadedBy = UserName(),
            Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
        };
        db.TechnicalDocuments.Add(document);
        db.TechnicalDocumentContents.Add(new TechnicalDocumentContent { TechnicalDocumentId = document.Id, Data = data });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(document));
    }

    [HttpGet("documents/{id:guid}/content")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await db.TechnicalDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        var content = await db.TechnicalDocumentContents.AsNoTracking().SingleOrDefaultAsync(c => c.TechnicalDocumentId == id, cancellationToken);
        if (document is null || content is null)
        {
            return NotFound();
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(content.Data, ContentTypeFor(document.FileName), document.FileName);
    }

    /// <summary>Withdraws a document (obsolete): it leaves the terminal and the product, stays in the history.</summary>
    [Authorize(Policy = "Engineering")]
    [HttpPost("documents/{id:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await db.TechnicalDocuments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        document.IsCurrent = false;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>What the operator of a phase needs at the terminal: the current documents of the product for that
    /// phase, plus those for the whole product.</summary>
    [HttpGet("work-orders/{workOrderId:guid}/operations/{operationId:guid}/documents")]
    public async Task<ActionResult<IEnumerable<TechnicalDocumentResponse>>> GetOperationDocuments(Guid workOrderId, Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = await db.WorkOrderOperations.AsNoTracking()
            .Where(o => o.Id == operationId && o.WorkOrderId == workOrderId)
            .Select(o => new { o.SequenceNumber, o.WorkOrder.ProductId })
            .SingleOrDefaultAsync(cancellationToken);
        if (operation is null)
        {
            return NotFound();
        }

        var documents = await db.TechnicalDocuments.AsNoTracking()
            .Where(d => d.ProductId == operation.ProductId && d.IsCurrent && (d.StepSequence == null || d.StepSequence == operation.SequenceNumber))
            .ToListAsync(cancellationToken);
        return Ok(documents
            .OrderBy(d => d.StepSequence is null ? 1 : 0).ThenBy(d => d.Kind == "instructions" ? 0 : 1).ThenBy(d => d.Title)
            .Select(ToResponse));
    }

    // ---------- Revisions ----------

    [HttpGet("products/{productId:guid}/revisions")]
    public async Task<ActionResult<ProductRevisionsResponse>> GetRevisions(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var archived = await db.ProductRevisions.AsNoTracking().Where(r => r.ProductId == productId).ToListAsync(cancellationToken);
        return Ok(new ProductRevisionsResponse(
            product.Revision,
            archived.OrderByDescending(r => r.ArchivedAt)
                .Select(r => new ProductRevisionResponse(r.Id, r.Revision, r.ArchivedAt, r.ReplacedByChangeId,
                    Deserialize<BillOfMaterialItemRequest>(r.BomJson), Deserialize<RoutingStepRequest>(r.RoutingJson)))
                .ToList()));
    }

    // ---------- Engineering changes ----------

    [HttpGet("changes")]
    public async Task<ActionResult<IEnumerable<EngineeringChangeSummary>>> GetChanges([FromQuery] string? status = null, [FromQuery] Guid? productId = null, CancellationToken cancellationToken = default)
    {
        var query = db.EngineeringChanges.AsNoTracking().Include(c => c.Product).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => c.Status == status);
        }

        if (productId is not null)
        {
            query = query.Where(c => c.ProductId == productId);
        }

        var changes = await query.OrderByDescending(c => c.Number).Take(500).ToListAsync(cancellationToken);
        return Ok(changes.Select(c => new EngineeringChangeSummary(c.Id, c.Number, c.ProductId, c.Product.Code, c.Product.Name, c.Title, c.Status,
            c.FromRevision, c.ToRevision, c.RequestedBy, c.CreatedAt, c.NewBomJson is not null, c.NewRoutingJson is not null)));
    }

    [HttpGet("changes/{id:guid}")]
    public async Task<ActionResult<EngineeringChangeResponse>> GetChange(Guid id, CancellationToken cancellationToken = default)
    {
        var change = await db.EngineeringChanges.AsNoTracking()
            .Include(c => c.Product).ThenInclude(p => p.BillOfMaterial)
            .Include(c => c.Product).ThenInclude(p => p.RoutingSteps)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (change is null)
        {
            return NotFound();
        }

        var affected = await db.WorkOrders.AsNoTracking()
            .Where(w => w.ProductId == change.ProductId && w.Status != "Completed" && w.Status != "Cancelled")
            .OrderBy(w => w.Code)
            .Select(w => new AffectedWorkOrder(w.Id, w.Code, w.Status, w.ProductRevision))
            .ToListAsync(cancellationToken);

        return Ok(ToResponse(change, affected));
    }

    [Authorize(Policy = "Engineering")]
    [HttpPost("changes")]
    public async Task<ActionResult<EngineeringChangeResponse>> CreateChange(EngineeringChangeRequest request, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
        if (product is null)
        {
            return BadRequest(new { message = "Prodotto non trovato." });
        }

        var error = await ValidateAsync(request, cancellationToken);
        if (error is not null)
        {
            return BadRequest(error);
        }

        var change = new EngineeringChange
        {
            Number = (await db.EngineeringChanges.MaxAsync(c => (int?)c.Number, cancellationToken) ?? 0) + 1,
            ProductId = product.Id,
            RequestedBy = UserName(),
            FromRevision = product.Revision,
        };
        Fill(change, request);
        db.EngineeringChanges.Add(change);
        await db.SaveChangesAsync(cancellationToken);
        return await GetChange(change.Id, cancellationToken);
    }

    [Authorize(Policy = "Engineering")]
    [HttpPut("changes/{id:guid}")]
    public async Task<ActionResult<EngineeringChangeResponse>> UpdateChange(Guid id, EngineeringChangeRequest request, CancellationToken cancellationToken = default)
    {
        var change = await db.EngineeringChanges.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (change is null)
        {
            return NotFound();
        }

        if (change.Status != EngineeringChangeStatus.Draft)
        {
            return Conflict(new { message = "Si modificano solo le modifiche tecniche in bozza." });
        }

        var error = await ValidateAsync(request with { ProductId = change.ProductId }, cancellationToken);
        if (error is not null)
        {
            return BadRequest(error);
        }

        Fill(change, request);
        await db.SaveChangesAsync(cancellationToken);
        return await GetChange(id, cancellationToken);
    }

    [Authorize(Policy = "Engineering")]
    [HttpPost("changes/{id:guid}/approve")]
    public Task<ActionResult<EngineeringChangeResponse>> Approve(Guid id, CancellationToken cancellationToken = default) =>
        MoveAsync(id, EngineeringChangeStatus.Draft, EngineeringChangeStatus.Approved, cancellationToken);

    [Authorize(Policy = "Engineering")]
    [HttpPost("changes/{id:guid}/reject")]
    public async Task<ActionResult<EngineeringChangeResponse>> Reject(Guid id, CancellationToken cancellationToken = default)
    {
        var change = await db.EngineeringChanges.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (change is null)
        {
            return NotFound();
        }

        if (change.Status is not (EngineeringChangeStatus.Draft or EngineeringChangeStatus.Approved))
        {
            return Conflict(new { message = "Una modifica già applicata non si può respingere." });
        }

        change.Status = EngineeringChangeStatus.Rejected;
        await db.SaveChangesAsync(cancellationToken);
        return await GetChange(id, cancellationToken);
    }

    /// <summary>Applies an approved change: archives revision N, writes the new BOM/routing, moves to N+1 and
    /// rebuilds the draft work orders of the product. Released or running jobs are only reported.</summary>
    [Authorize(Policy = "Engineering")]
    [HttpPost("changes/{id:guid}/apply")]
    public async Task<ActionResult<EngineeringChangeResponse>> Apply(Guid id, CancellationToken cancellationToken = default)
    {
        var change = await db.EngineeringChanges.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (change is null)
        {
            return NotFound();
        }

        if (change.Status != EngineeringChangeStatus.Approved)
        {
            return Conflict(new { message = "Prima di applicarla la modifica va approvata." });
        }

        var product = await db.Products
            .Include(p => p.BillOfMaterial)
            .Include(p => p.RoutingSteps)
            .SingleAsync(p => p.Id == change.ProductId, cancellationToken);

        var newBom = change.NewBomJson is null ? null : Deserialize<BillOfMaterialItemRequest>(change.NewBomJson);
        var newRouting = change.NewRoutingJson is null ? null : Deserialize<RoutingStepRequest>(change.NewRoutingJson);

        // Materials may have been deactivated since the change was written.
        if (newBom is not null)
        {
            var error = await UnknownMaterialsAsync(newBom, cancellationToken);
            if (error is not null)
            {
                return BadRequest(error);
            }
        }

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;

        var fromRevision = product.Revision;
        var toRevision = NextRevision(fromRevision);
        db.ProductRevisions.Add(new ProductRevision
        {
            ProductId = product.Id,
            Revision = fromRevision,
            BomJson = JsonSerializer.Serialize(product.BillOfMaterial.Select(b => new BillOfMaterialItemRequest(b.MaterialCode, b.Quantity, b.Notes)), Json),
            RoutingJson = JsonSerializer.Serialize(product.RoutingSteps.OrderBy(s => s.SequenceNumber).Select(s => new RoutingStepRequest(s.Name, s.Description, s.WorkCenter, s.EstimatedMinutes)), Json),
            ReplacedByChangeId = change.Id,
        });

        if (newBom is not null)
        {
            foreach (var item in product.BillOfMaterial.ToList())
            {
                db.BillOfMaterialItems.Remove(item);
            }

            foreach (var item in newBom)
            {
                db.BillOfMaterialItems.Add(new BillOfMaterialItem { ProductId = product.Id, MaterialCode = item.MaterialCode.Trim(), Quantity = item.Quantity, Notes = Clean(item.Notes) });
            }
        }

        var steps = product.RoutingSteps.OrderBy(s => s.SequenceNumber).Select(s => new RoutingStepRequest(s.Name, s.Description, s.WorkCenter, s.EstimatedMinutes)).ToList();
        if (newRouting is not null)
        {
            foreach (var step in product.RoutingSteps.ToList())
            {
                db.RoutingSteps.Remove(step);
            }

            var sequence = 1;
            foreach (var step in newRouting)
            {
                db.RoutingSteps.Add(new RoutingStep { ProductId = product.Id, SequenceNumber = sequence++, Name = step.Name.Trim(), Description = Clean(step.Description), WorkCenter = Clean(step.WorkCenter), EstimatedMinutes = step.EstimatedMinutes });
            }

            steps = newRouting;
        }

        product.Revision = toRevision;

        // Open work orders of the product: drafts follow the new revision; the others are reported.
        var openOrders = await db.WorkOrders
            .Where(w => w.ProductId == product.Id && w.Status != "Completed" && w.Status != "Cancelled")
            .ToListAsync(cancellationToken);
        var updated = new List<string>();
        var untouched = new List<string>();
        foreach (var order in openOrders)
        {
            if (order.Status != "Draft")
            {
                untouched.Add($"{order.Code} ({order.Status}, rev. {order.ProductRevision ?? "?"})");
                continue;
            }

            if (newRouting is not null)
            {
                await RebuildOperationsAsync(order, steps, cancellationToken);
            }

            order.ProductRevision = toRevision;
            updated.Add(order.Code);
        }

        change.Status = EngineeringChangeStatus.Applied;
        change.FromRevision = fromRevision;
        change.ToRevision = toRevision;
        change.AppliedAt = DateTime.UtcNow;
        change.AppliedBy = UserName();
        change.ApplyReport = (updated.Count == 0 ? "Nessuna commessa in bozza da aggiornare." : $"Commesse in bozza aggiornate alla rev. {toRevision}: {string.Join(", ", updated)}.")
            + (untouched.Count == 0 ? string.Empty : $" Commesse già rilasciate, non modificate (da valutare in reparto): {string.Join(", ", untouched)}.");

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetChange(id, cancellationToken);
    }

    // ---------- Helpers ----------

    /// <summary>Replaces the phases of a draft work order (and each unit's phase grid) with the new routing.
    /// New rows are added explicitly: their keys are preset, so EF must not guess from a tracked collection.</summary>
    private async Task RebuildOperationsAsync(WorkOrder order, IReadOnlyList<RoutingStepRequest> steps, CancellationToken cancellationToken)
    {
        var operations = await db.WorkOrderOperations.Where(o => o.WorkOrderId == order.Id).ToListAsync(cancellationToken);
        var operationIds = operations.Select(o => o.Id).ToList();
        var unitOperations = await db.WorkOrderUnitOperations.Where(u => operationIds.Contains(u.WorkOrderOperationId)).ToListAsync(cancellationToken);
        db.WorkOrderUnitOperations.RemoveRange(unitOperations);
        db.WorkOrderOperations.RemoveRange(operations);

        var units = await db.WorkOrderUnits.Where(u => u.WorkOrderId == order.Id).Select(u => u.Id).ToListAsync(cancellationToken);
        var sequence = 1;
        foreach (var step in steps)
        {
            var operation = new WorkOrderOperation
            {
                WorkOrderId = order.Id,
                SequenceNumber = sequence++,
                Name = step.Name.Trim(),
                Description = Clean(step.Description),
                WorkCenter = Clean(step.WorkCenter),
                EstimatedMinutes = step.EstimatedMinutes,
                Status = "Pending",
            };
            db.WorkOrderOperations.Add(operation);
            foreach (var unitId in units)
            {
                db.WorkOrderUnitOperations.Add(new WorkOrderUnitOperation { WorkOrderUnitId = unitId, WorkOrderOperationId = operation.Id, Status = "Pending" });
            }
        }
    }

    private async Task<ActionResult<EngineeringChangeResponse>> MoveAsync(Guid id, string from, string to, CancellationToken cancellationToken)
    {
        var change = await db.EngineeringChanges.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (change is null)
        {
            return NotFound();
        }

        if (change.Status != from)
        {
            return Conflict(new { message = "La modifica non è nello stato giusto per questa operazione." });
        }

        change.Status = to;
        change.ApprovedBy = UserName();
        change.ApprovedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await GetChange(id, cancellationToken);
    }

    private async Task<object?> ValidateAsync(EngineeringChangeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return new { message = "Scrivi un titolo per la modifica." };
        }

        if (request.Bom is null && request.Routing is null)
        {
            return new { message = "La modifica deve cambiare la distinta, il ciclo o entrambi." };
        }

        if (request.Bom is not null)
        {
            if (request.Bom.Count == 0 || request.Bom.Any(i => string.IsNullOrWhiteSpace(i.MaterialCode) || i.Quantity <= 0))
            {
                return new { message = "Ogni riga della distinta deve avere codice materiale e quantità maggiore di zero." };
            }

            var unknown = await UnknownMaterialsAsync(request.Bom, cancellationToken);
            if (unknown is not null)
            {
                return unknown;
            }
        }

        if (request.Routing is not null && (request.Routing.Count == 0 || request.Routing.Any(s => string.IsNullOrWhiteSpace(s.Name) || s.EstimatedMinutes < 0)))
        {
            return new { message = "Ogni fase del ciclo deve avere un nome e una durata non negativa." };
        }

        return null;
    }

    private async Task<object?> UnknownMaterialsAsync(IEnumerable<BillOfMaterialItemRequest> items, CancellationToken cancellationToken)
    {
        var codes = items.Select(i => i.MaterialCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var known = (await db.Materials.Where(m => m.IsActive && codes.Contains(m.Code)).Select(m => m.Code).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = codes.Where(c => !known.Contains(c)).ToArray();
        return unknown.Length == 0 ? null : new { message = "Codici materiale non trovati o non attivi nel catalogo.", materials = unknown };
    }

    private static void Fill(EngineeringChange change, EngineeringChangeRequest request)
    {
        change.Title = request.Title!.Trim();
        change.Description = Clean(request.Description);
        change.Reason = Clean(request.Reason);
        change.NewBomJson = request.Bom is null ? null : JsonSerializer.Serialize(request.Bom, Json);
        change.NewRoutingJson = request.Routing is null ? null : JsonSerializer.Serialize(request.Routing, Json);
    }

    /// <summary>A → B → … → Z → AA → AB. Anything else (e.g. "01") gets a numeric suffix bump or "-1".</summary>
    public static string NextRevision(string current)
    {
        current = string.IsNullOrWhiteSpace(current) ? "A" : current.Trim().ToUpperInvariant();
        if (current.All(c => c is >= 'A' and <= 'Z'))
        {
            var chars = current.ToCharArray();
            for (var i = chars.Length - 1; i >= 0; i--)
            {
                if (chars[i] != 'Z')
                {
                    chars[i]++;
                    return new string(chars);
                }

                chars[i] = 'A';
            }

            return "A" + new string(chars);
        }

        var digits = new string(current.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return digits.Length == 0
            ? current + "-1"
            : current[..^digits.Length] + (long.Parse(digits) + 1).ToString().PadLeft(digits.Length, '0');
    }

    private static List<T> Deserialize<T>(string json) => JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string? UserName() => User.FindFirstValue(ClaimTypes.Name);

    private static TechnicalDocumentResponse ToResponse(TechnicalDocument d) =>
        new(d.Id, d.ProductId, d.StepSequence, d.Kind, TechnicalDocumentKinds.All.GetValueOrDefault(d.Kind, d.Kind), d.Title, d.FileName, d.ContentType,
            d.SizeBytes, d.Version, d.VersionNote, d.IsCurrent, d.UploadedBy, d.UploadedAt);

    private static EngineeringChangeResponse ToResponse(EngineeringChange c, IReadOnlyList<AffectedWorkOrder> affected) =>
        new(c.Id, c.Number, c.ProductId, c.Product.Code, c.Product.Name, c.Product.Revision, c.Title, c.Description, c.Reason, c.Status,
            c.FromRevision, c.ToRevision, c.RequestedBy, c.CreatedAt, c.ApprovedBy, c.ApprovedAt, c.AppliedBy, c.AppliedAt, c.ApplyReport,
            c.Product.BillOfMaterial.Select(b => new BillOfMaterialItemRequest(b.MaterialCode, b.Quantity, b.Notes)).ToList(),
            c.Product.RoutingSteps.OrderBy(s => s.SequenceNumber).Select(s => new RoutingStepRequest(s.Name, s.Description, s.WorkCenter, s.EstimatedMinutes)).ToList(),
            c.NewBomJson is null ? null : Deserialize<BillOfMaterialItemRequest>(c.NewBomJson),
            c.NewRoutingJson is null ? null : Deserialize<RoutingStepRequest>(c.NewRoutingJson),
            affected);
}

public sealed record TechnicalDocumentResponse(Guid Id, Guid ProductId, int? StepSequence, string Kind, string KindName, string Title, string FileName,
    string ContentType, long SizeBytes, int Version, string? VersionNote, bool IsCurrent, string? UploadedBy, DateTime UploadedAt);

public sealed record ProductRevisionResponse(Guid Id, string Revision, DateTime ArchivedAt, Guid? ReplacedByChangeId,
    IReadOnlyList<BillOfMaterialItemRequest> Bom, IReadOnlyList<RoutingStepRequest> Routing);

public sealed record ProductRevisionsResponse(string CurrentRevision, IReadOnlyList<ProductRevisionResponse> Archived);

public sealed record EngineeringChangeRequest(Guid ProductId, string? Title, string? Description, string? Reason,
    List<BillOfMaterialItemRequest>? Bom, List<RoutingStepRequest>? Routing);

public sealed record EngineeringChangeSummary(Guid Id, int Number, Guid ProductId, string ProductCode, string ProductName, string Title, string Status,
    string? FromRevision, string? ToRevision, string? RequestedBy, DateTime CreatedAt, bool ChangesBom, bool ChangesRouting);

public sealed record AffectedWorkOrder(Guid Id, string Code, string Status, string? ProductRevision);

public sealed record EngineeringChangeResponse(Guid Id, int Number, Guid ProductId, string ProductCode, string ProductName, string ProductRevision,
    string Title, string? Description, string? Reason, string Status, string? FromRevision, string? ToRevision, string? RequestedBy, DateTime CreatedAt,
    string? ApprovedBy, DateTime? ApprovedAt, string? AppliedBy, DateTime? AppliedAt, string? ApplyReport,
    IReadOnlyList<BillOfMaterialItemRequest> CurrentBom, IReadOnlyList<RoutingStepRequest> CurrentRouting,
    IReadOnlyList<BillOfMaterialItemRequest>? ProposedBom, IReadOnlyList<RoutingStepRequest>? ProposedRouting,
    IReadOnlyList<AffectedWorkOrder> AffectedWorkOrders);
