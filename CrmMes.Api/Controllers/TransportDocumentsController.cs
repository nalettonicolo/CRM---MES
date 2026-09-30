using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Documenti di trasporto (DDT) and conto lavoro. See TransportDocument for the lifecycle:
/// drafts are edited freely, issuing assigns the yearly progressive number, issued documents are only
/// ever cancelled. A document with causale "Conto lavorazione" also tracks what the subcontractor still
/// has to send back.</summary>
[ApiController]
[Authorize]
[Route("api/transport-documents")]
public class TransportDocumentsController : ControllerBase
{
    private const int MaxNumberingAttempts = 3;
    private readonly ApplicationDbContext _dbContext;

    public TransportDocumentsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("reasons")]
    public ActionResult<IEnumerable<TransportReasonResponse>> GetReasons() =>
        Ok(TransportReasons.Labels.Select(pair => new TransportReasonResponse(pair.Key, pair.Value)));

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TransportDocumentSummaryResponse>>> GetDocuments(
        [FromQuery] string? status = null,
        [FromQuery] string? reason = null,
        [FromQuery] string? search = null,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.TransportDocuments.AsNoTracking().Include(document => document.Lines).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(document => document.Status == status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            query = query.Where(document => document.Reason == reason.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(document => document.RecipientName.ToLower().Contains(term)
                || (document.Notes != null && document.Notes.ToLower().Contains(term)));
        }

        var documents = await query
            .OrderByDescending(document => document.Year)
            .ThenByDescending(document => document.Number)
            .ThenByDescending(document => document.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return Ok(documents.Select(document => new TransportDocumentSummaryResponse(
            document.Id, DocumentCode(document), document.Number, document.Year, document.Status, document.Reason,
            TransportReasons.Label(document.Reason, document.ReasonDetail), document.RecipientName,
            document.IssuedAt, document.CreatedAt, document.Lines.Count, document.WorkOrderId)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransportDocumentResponse>> GetDocument(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(id, tracking: false, cancellationToken);
        return document is null ? NotFound() : Ok(ToResponse(document));
    }

    [Authorize(Policy = "TransportDocuments")]
    [HttpPost]
    public async Task<ActionResult<TransportDocumentResponse>> CreateDraft(
        SaveTransportDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var document = new TransportDocument { CreatedBy = User.FindFirstValue(ClaimTypes.Name) };
        var error = await ApplyAsync(document, request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        _dbContext.TransportDocuments.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, ToResponse((await LoadAsync(document.Id, false, cancellationToken))!));
    }

    /// <summary>A draft prefilled from a work order: its customer as recipient and the finished product,
    /// with lot and quantity, as the only line. Then edited like any draft.</summary>
    [Authorize(Policy = "TransportDocuments")]
    [HttpPost("from-work-order/{workOrderId:guid}")]
    public async Task<ActionResult<TransportDocumentResponse>> CreateFromWorkOrder(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var workOrder = await _dbContext.WorkOrders.AsNoTracking()
            .Include(order => order.Product)
            .Include(order => order.Customer)
            .FirstOrDefaultAsync(order => order.Id == workOrderId, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }

        var document = new TransportDocument
        {
            CreatedBy = User.FindFirstValue(ClaimTypes.Name),
            Reason = "Sale",
            WorkOrderId = workOrder.Id,
            CustomerId = workOrder.CustomerId,
            RecipientName = workOrder.Customer?.Name ?? workOrder.CustomerReference ?? string.Empty,
            RecipientAddress = workOrder.Customer?.Address,
            RecipientVatNumber = workOrder.Customer?.VatNumber,
            Port = "Franco",
            Lines =
            [
                new TransportDocumentLine
                {
                    LineNumber = 1,
                    ProductId = workOrder.ProductId,
                    Code = workOrder.Product.Code,
                    Description = workOrder.Product.Name,
                    Quantity = workOrder.Quantity,
                    Unit = "pz",
                    LotNumber = string.IsNullOrWhiteSpace(workOrder.ProductLotNumber) ? null : workOrder.ProductLotNumber,
                    Notes = $"Commessa {workOrder.Code}"
                }
            ]
        };

        _dbContext.TransportDocuments.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, ToResponse((await LoadAsync(document.Id, false, cancellationToken))!));
    }

    [Authorize(Policy = "TransportDocuments")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TransportDocumentResponse>> UpdateDraft(
        Guid id, SaveTransportDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(id, tracking: true, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (document.Status != "Draft")
        {
            return Conflict(new { message = "Un DDT emesso non si modifica: annullalo e creane uno nuovo." });
        }

        var error = await ApplyAsync(document, request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse((await LoadAsync(id, false, cancellationToken))!));
    }

    [Authorize(Policy = "TransportDocuments")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.TransportDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (document.Status != "Draft")
        {
            return Conflict(new { message = "Si eliminano solo le bozze: un DDT emesso si annulla." });
        }

        _dbContext.TransportDocuments.Remove(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Assigns the next number of the year (Italian time) and freezes the document. The unique
    /// (year, number) index settles two issues at the same instant: the loser retries with the next number.</summary>
    [Authorize(Policy = "TransportDocuments")]
    [HttpPost("{id:guid}/issue")]
    public async Task<ActionResult<TransportDocumentResponse>> Issue(
        Guid id, IssueTransportDocumentRequest? request, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var document = await _dbContext.TransportDocuments.Include(d => d.Lines)
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (document is null)
            {
                return NotFound();
            }

            if (document.Status != "Draft")
            {
                return Conflict(new { message = "Il DDT è già stato emesso." });
            }

            if (document.Lines.Count == 0)
            {
                return BadRequest(new { message = "Aggiungi almeno una riga prima di emettere il DDT." });
            }

            var now = DateTime.UtcNow;
            var year = ItalianTime(now).Year;
            var last = await _dbContext.TransportDocuments
                .Where(d => d.Year == year && d.Number != null)
                .MaxAsync(d => d.Number, cancellationToken);

            document.Year = year;
            document.Number = (last ?? 0) + 1;
            document.Status = "Issued";
            document.IssuedAt = now;
            document.IssuedBy = User.FindFirstValue(ClaimTypes.Name);
            document.TransportStartAt = request?.TransportStartAt ?? document.TransportStartAt ?? now;
            _dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "TransportDocumentIssued",
                EntityType = "TransportDocument",
                EntityId = document.Id,
                UserName = document.IssuedBy,
                Details = $"DDT {DocumentCode(document)} emesso ({TransportReasons.Label(document.Reason, document.ReasonDetail)}) a {document.RecipientName}."
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Ok(ToResponse((await LoadAsync(id, false, cancellationToken))!));
            }
            catch (DbUpdateException) when (attempt < MaxNumberingAttempts)
            {
                // Someone took the same number: forget the attempted change and read the sequence again.
                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    [Authorize(Policy = "TransportDocuments")]
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<TransportDocumentResponse>> Cancel(
        Guid id, CancelTransportDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(id, tracking: true, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (document.Status != "Issued")
        {
            return Conflict(new { message = document.Status == "Draft" ? "Una bozza si elimina, non si annulla." : "Il DDT è già annullato." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { message = "Indica il motivo dell'annullamento." });
        }

        if (document.Lines.Any(line => line.Returns.Count > 0))
        {
            return Conflict(new { message = "Il DDT ha rientri registrati dal terzista: elimina prima i rientri." });
        }

        document.Status = "Cancelled";
        document.CancelledAt = DateTime.UtcNow;
        document.CancellationReason = request.Reason.Trim();
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "TransportDocumentCancelled",
            EntityType = "TransportDocument",
            EntityId = document.Id,
            UserName = User.FindFirstValue(ClaimTypes.Name),
            Details = $"DDT {DocumentCode(document)} annullato: {document.CancellationReason}"
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(document));
    }

    /// <summary>Records goods coming back from the subcontractor (or scrapped by them) against one line.</summary>
    [Authorize(Policy = "TransportDocuments")]
    [HttpPost("{id:guid}/lines/{lineId:guid}/returns")]
    public async Task<ActionResult<TransportDocumentResponse>> AddReturn(
        Guid id, Guid lineId, AddSubcontractingReturnRequest request, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(id, tracking: true, cancellationToken);
        var line = document?.Lines.FirstOrDefault(l => l.Id == lineId);
        if (document is null || line is null)
        {
            return NotFound();
        }

        if (document.Reason != TransportReasons.Subcontracting || document.Status != "Issued")
        {
            return Conflict(new { message = "I rientri si registrano solo su DDT di conto lavorazione emessi." });
        }

        if (request.Quantity < 0 || request.ScrapQuantity < 0 || request.Quantity + request.ScrapQuantity <= 0)
        {
            return BadRequest(new { message = "Indica una quantità rientrata o scartata maggiore di zero." });
        }

        var outstanding = Outstanding(line);
        if (request.Quantity + request.ScrapQuantity > outstanding)
        {
            return BadRequest(new { message = $"Rientro superiore al residuo presso il terzista ({outstanding:0.###} {line.Unit})." });
        }

        _dbContext.SubcontractingReturns.Add(new SubcontractingReturn
        {
            TransportDocumentLineId = line.Id,
            Quantity = request.Quantity,
            ScrapQuantity = request.ScrapQuantity,
            ReturnedAt = request.ReturnedAt ?? DateTime.UtcNow,
            SupplierDocumentReference = Clean(request.SupplierDocumentReference),
            Notes = Clean(request.Notes),
            RecordedBy = User.FindFirstValue(ClaimTypes.Name)
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse((await LoadAsync(id, false, cancellationToken))!));
    }

    [Authorize(Policy = "TransportDocuments")]
    [HttpDelete("{id:guid}/returns/{returnId:guid}")]
    public async Task<ActionResult<TransportDocumentResponse>> DeleteReturn(Guid id, Guid returnId, CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.SubcontractingReturns
            .FirstOrDefaultAsync(r => r.Id == returnId && r.Line.TransportDocumentId == id, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        _dbContext.SubcontractingReturns.Remove(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse((await LoadAsync(id, false, cancellationToken))!));
    }

    /// <summary>Conto lavoro: everything still at subcontractors, oldest first, with what is overdue.</summary>
    [HttpGet("~/api/subcontracting/open")]
    public async Task<ActionResult<IEnumerable<SubcontractingOpenLineResponse>>> GetOpenSubcontracting(
        [FromQuery] Guid? supplierId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.TransportDocuments.AsNoTracking()
            .Include(d => d.Supplier)
            .Include(d => d.Lines).ThenInclude(l => l.Returns)
            .Where(d => d.Reason == TransportReasons.Subcontracting && d.Status == "Issued");
        if (supplierId.HasValue)
        {
            query = query.Where(d => d.SupplierId == supplierId);
        }

        var today = DateTime.UtcNow.Date;
        var documents = await query.ToListAsync(cancellationToken);
        var rows = documents
            .SelectMany(document => document.Lines.Select(line => (document, line, outstanding: Outstanding(line))))
            .Where(row => row.outstanding > 0)
            .OrderBy(row => row.document.IssuedAt)
            .Select(row => new SubcontractingOpenLineResponse(
                row.document.Id, DocumentCode(row.document), row.line.Id, row.document.SupplierId,
                row.document.Supplier?.Name ?? row.document.RecipientName, row.document.IssuedAt, row.document.ExpectedReturnAt,
                row.document.ExpectedReturnAt.HasValue && row.document.ExpectedReturnAt.Value.Date < today,
                row.line.Code, row.line.Description, row.line.Unit, row.line.Quantity,
                row.line.Returns.Sum(r => r.Quantity), row.line.Returns.Sum(r => r.ScrapQuantity), row.outstanding))
            .ToList();
        return Ok(rows);
    }

    /// <summary>Issued DDT lines of a period, one row per line with everything an accounting system
    /// needs for deferred invoicing (fatturazione differita): document number and date, customer with VAT
    /// number, causale, goods and quantities. Cancelled documents are left out. Dates are inclusive days.</summary>
    [HttpGet("export")]
    public async Task<ActionResult<IEnumerable<TransportDocumentExportRow>>> Export(
        [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return BadRequest(new { message = "La data finale precede quella iniziale." });
        }

        // Days are Italian calendar days: a DDT issued at 00:30 on the 1st belongs to the 1st.
        var fromUtc = ItalianMidnightToUtc(from);
        var toUtc = ItalianMidnightToUtc(to.AddDays(1));
        var query = _dbContext.TransportDocuments.AsNoTracking()
            .Include(d => d.Customer)
            .Include(d => d.WorkOrder)
            .Include(d => d.Lines)
            .Where(d => d.Status == "Issued" && d.IssuedAt >= fromUtc && d.IssuedAt < toUtc);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            query = query.Where(d => d.Reason == reason.Trim());
        }

        var documents = await query.ToListAsync(cancellationToken);
        return Ok(documents
            .OrderBy(d => d.Year).ThenBy(d => d.Number)
            .SelectMany(d => d.Lines.OrderBy(l => l.LineNumber).Select(l => new TransportDocumentExportRow(
                d.Number!.Value, d.Year!.Value, d.IssuedAt!.Value, TransportReasons.Label(d.Reason, d.ReasonDetail),
                d.Customer?.Code, d.RecipientName, d.RecipientVatNumber, d.WorkOrder?.Code,
                l.LineNumber, l.Code, l.Description, l.Unit, l.Quantity, l.LotNumber))));
    }

    private async Task<ActionResult?> ApplyAsync(TransportDocument document, SaveTransportDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!TransportReasons.IsKnown(request.Reason))
        {
            return BadRequest(new { message = "Causale non riconosciuta." });
        }

        if (request.Reason == TransportReasons.Other && string.IsNullOrWhiteSpace(request.ReasonDetail))
        {
            return BadRequest(new { message = "Con causale \"Altro\" scrivi la causale." });
        }

        var transportBy = request.TransportBy ?? "Sender";
        if (!TransportReasons.TransportBy.Contains(transportBy))
        {
            return BadRequest(new { message = "Trasporto a cura di: mittente, destinatario o vettore." });
        }

        if (transportBy == "Carrier" && request.CarrierId is null)
        {
            return BadRequest(new { message = "Con trasporto a cura del vettore indica il vettore." });
        }

        if (!string.IsNullOrWhiteSpace(request.Port) && !TransportReasons.Ports.Contains(request.Port))
        {
            return BadRequest(new { message = "Porto: Franco o Assegnato." });
        }

        if (request.Packages < 0 || request.GrossWeightKg < 0)
        {
            return BadRequest(new { message = "Colli e peso non possono essere negativi." });
        }

        Customer? customer = null;
        if (request.CustomerId is { } customerId
            && (customer = await _dbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)) is null)
        {
            return BadRequest(new { message = "Cliente non trovato." });
        }

        Supplier? supplier = null;
        if (request.SupplierId is { } supplierId
            && (supplier = await _dbContext.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken)) is null)
        {
            return BadRequest(new { message = "Fornitore non trovato." });
        }

        if (request.Reason == TransportReasons.Subcontracting && supplier is null)
        {
            return BadRequest(new { message = "Per il conto lavorazione indica il terzista tra i fornitori." });
        }

        if (request.CarrierId is { } carrierId && !await _dbContext.Carriers.AnyAsync(c => c.Id == carrierId, cancellationToken))
        {
            return BadRequest(new { message = "Vettore non trovato." });
        }

        if (request.WorkOrderId is { } workOrderId && !await _dbContext.WorkOrders.AnyAsync(w => w.Id == workOrderId, cancellationToken))
        {
            return BadRequest(new { message = "Commessa non trovata." });
        }

        var recipient = Clean(request.RecipientName) ?? customer?.Name ?? supplier?.Name;
        if (recipient is null)
        {
            return BadRequest(new { message = "Indica il destinatario." });
        }

        var lines = request.Lines ?? [];
        if (lines.Count == 0)
        {
            return BadRequest(new { message = "Aggiungi almeno una riga." });
        }

        var materialIds = lines.Where(l => l.MaterialId.HasValue).Select(l => l.MaterialId!.Value).Distinct().ToList();
        var productIds = lines.Where(l => l.ProductId.HasValue).Select(l => l.ProductId!.Value).Distinct().ToList();
        var materials = await _dbContext.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
        var products = await _dbContext.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        var newLines = new List<TransportDocumentLine>();
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            if (line.MaterialId.HasValue && line.ProductId.HasValue)
            {
                return BadRequest(new { message = $"Riga {index + 1}: indica un materiale o un prodotto, non entrambi." });
            }

            Material? material = null;
            Product? product = null;
            if ((line.MaterialId is { } materialId && !materials.TryGetValue(materialId, out material))
                || (line.ProductId is { } productId && !products.TryGetValue(productId, out product)))
            {
                return BadRequest(new { message = $"Riga {index + 1}: articolo non trovato." });
            }

            if (line.Quantity <= 0)
            {
                return BadRequest(new { message = $"Riga {index + 1}: la quantità deve essere maggiore di zero." });
            }

            var description = Clean(line.Description) ?? material?.Name ?? product?.Name;
            if (description is null)
            {
                return BadRequest(new { message = $"Riga {index + 1}: manca la descrizione." });
            }

            newLines.Add(new TransportDocumentLine
            {
                LineNumber = index + 1,
                MaterialId = material?.Id,
                ProductId = product?.Id,
                Code = Clean(line.Code) ?? material?.Code ?? product?.Code,
                Description = description,
                Quantity = line.Quantity,
                Unit = Clean(line.Unit) ?? material?.Unit ?? "pz",
                LotNumber = Clean(line.LotNumber),
                Notes = Clean(line.Notes)
            });
        }

        document.Reason = request.Reason!;
        document.ReasonDetail = request.Reason == TransportReasons.Other ? Clean(request.ReasonDetail) : null;
        document.CustomerId = customer?.Id;
        document.SupplierId = supplier?.Id;
        document.RecipientName = recipient;
        document.RecipientAddress = Clean(request.RecipientAddress) ?? customer?.Address;
        document.RecipientVatNumber = Clean(request.RecipientVatNumber) ?? customer?.VatNumber;
        document.DestinationAddress = Clean(request.DestinationAddress);
        document.TransportBy = transportBy;
        document.CarrierId = transportBy == "Carrier" ? request.CarrierId : null;
        document.Port = Clean(request.Port);
        document.GoodsAppearance = Clean(request.GoodsAppearance);
        document.Packages = request.Packages;
        document.GrossWeightKg = request.GrossWeightKg;
        document.TransportStartAt = request.TransportStartAt;
        document.ExpectedReturnAt = request.Reason == TransportReasons.Subcontracting ? request.ExpectedReturnAt : null;
        document.WorkOrderId = request.WorkOrderId;
        document.Notes = Clean(request.Notes);

        // Lines are replaced as a whole: a draft has no returns yet, so nothing hangs off them.
        foreach (var old in document.Lines.ToList())
        {
            _dbContext.TransportDocumentLines.Remove(old);
        }

        document.Lines.Clear();
        var tracked = _dbContext.Entry(document).State != EntityState.Detached;
        foreach (var line in newLines)
        {
            document.Lines.Add(line);
            if (tracked)
            {
                // The Guid key is already set, so EF would take the new line for an existing row to update.
                _dbContext.Entry(line).State = EntityState.Added;
            }
        }

        return null;
    }

    private Task<TransportDocument?> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = _dbContext.TransportDocuments
            .Include(d => d.Carrier)
            .Include(d => d.WorkOrder)
            .Include(d => d.Lines).ThenInclude(l => l.Returns)
            .AsSplitQuery();
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    private static decimal Outstanding(TransportDocumentLine line) =>
        line.Quantity - line.Returns.Sum(r => r.Quantity + r.ScrapQuantity);

    /// <summary>"12/2026" once issued, "Bozza" before.</summary>
    public static string DocumentCode(TransportDocument document) =>
        document.Number is { } number && document.Year is { } year ? $"{number}/{year}" : "Bozza";

    private static DateTime ItalianMidnightToUtc(DateTime day)
    {
        var midnight = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
        try
        {
            return TimeZoneInfo.ConvertTimeToUtc(midnight, TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"));
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTime.SpecifyKind(midnight, DateTimeKind.Utc);
        }
    }

    private static DateTime ItalianTime(DateTime utc)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome"));
        }
        catch (TimeZoneNotFoundException)
        {
            return utc;
        }
    }

    private static TransportDocumentResponse ToResponse(TransportDocument document)
    {
        var subcontracting = document.Reason == TransportReasons.Subcontracting;
        return new TransportDocumentResponse(
            document.Id, DocumentCode(document), document.Number, document.Year, document.Status,
            document.Reason, document.ReasonDetail, TransportReasons.Label(document.Reason, document.ReasonDetail),
            document.CustomerId, document.SupplierId, document.RecipientName, document.RecipientAddress, document.RecipientVatNumber,
            document.DestinationAddress, document.TransportBy, document.CarrierId, document.Carrier?.Name,
            document.Port, document.GoodsAppearance, document.Packages, document.GrossWeightKg, document.TransportStartAt,
            document.ExpectedReturnAt, document.WorkOrderId, document.WorkOrder?.Code, document.Notes,
            document.CancellationReason, document.CreatedAt, document.CreatedBy, document.IssuedAt, document.IssuedBy, document.CancelledAt,
            document.Lines.OrderBy(l => l.LineNumber).Select(line => new TransportDocumentLineResponse(
                line.Id, line.LineNumber, line.MaterialId, line.ProductId, line.Code, line.Description, line.Quantity, line.Unit,
                line.LotNumber, line.Notes,
                subcontracting ? line.Returns.Sum(r => r.Quantity) : null,
                subcontracting ? line.Returns.Sum(r => r.ScrapQuantity) : null,
                subcontracting ? Outstanding(line) : null,
                line.Returns.OrderBy(r => r.ReturnedAt).Select(r => new SubcontractingReturnResponse(
                    r.Id, r.Quantity, r.ScrapQuantity, r.ReturnedAt, r.SupplierDocumentReference, r.Notes, r.RecordedBy)).ToList()))
                .ToList());
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TransportReasonResponse(string Key, string Label);

public sealed record SaveTransportDocumentLineRequest(
    Guid? MaterialId, Guid? ProductId, string? Code, string? Description, decimal Quantity, string? Unit,
    string? LotNumber, string? Notes);

public sealed record SaveTransportDocumentRequest(
    string? Reason, string? ReasonDetail, Guid? CustomerId, Guid? SupplierId,
    string? RecipientName, string? RecipientAddress, string? RecipientVatNumber, string? DestinationAddress,
    string? TransportBy, Guid? CarrierId, string? Port, string? GoodsAppearance, int? Packages, decimal? GrossWeightKg,
    DateTime? TransportStartAt, DateTime? ExpectedReturnAt, Guid? WorkOrderId, string? Notes,
    List<SaveTransportDocumentLineRequest>? Lines);

public sealed record IssueTransportDocumentRequest(DateTime? TransportStartAt);

public sealed record CancelTransportDocumentRequest(string? Reason);

public sealed record AddSubcontractingReturnRequest(
    decimal Quantity, decimal ScrapQuantity, DateTime? ReturnedAt, string? SupplierDocumentReference, string? Notes);

public sealed record TransportDocumentSummaryResponse(
    Guid Id, string DocumentCode, int? Number, int? Year, string Status, string Reason, string ReasonLabel,
    string RecipientName, DateTime? IssuedAt, DateTime CreatedAt, int LineCount, Guid? WorkOrderId);

public sealed record SubcontractingReturnResponse(
    Guid Id, decimal Quantity, decimal ScrapQuantity, DateTime ReturnedAt, string? SupplierDocumentReference,
    string? Notes, string? RecordedBy);

public sealed record TransportDocumentLineResponse(
    Guid Id, int LineNumber, Guid? MaterialId, Guid? ProductId, string? Code, string Description, decimal Quantity,
    string Unit, string? LotNumber, string? Notes, decimal? ReturnedQuantity, decimal? ScrapQuantity,
    decimal? OutstandingQuantity, List<SubcontractingReturnResponse> Returns);

public sealed record TransportDocumentResponse(
    Guid Id, string DocumentCode, int? Number, int? Year, string Status, string Reason, string? ReasonDetail,
    string ReasonLabel, Guid? CustomerId, Guid? SupplierId, string RecipientName, string? RecipientAddress,
    string? RecipientVatNumber, string? DestinationAddress, string TransportBy, Guid? CarrierId, string? CarrierName,
    string? Port, string? GoodsAppearance, int? Packages, decimal? GrossWeightKg, DateTime? TransportStartAt,
    DateTime? ExpectedReturnAt, Guid? WorkOrderId, string? WorkOrderCode, string? Notes, string? CancellationReason,
    DateTime CreatedAt, string? CreatedBy, DateTime? IssuedAt, string? IssuedBy, DateTime? CancelledAt,
    List<TransportDocumentLineResponse> Lines);

public sealed record SubcontractingOpenLineResponse(
    Guid DocumentId, string DocumentCode, Guid LineId, Guid? SupplierId, string SupplierName, DateTime? SentAt,
    DateTime? ExpectedReturnAt, bool IsOverdue, string? Code, string Description, string Unit, decimal SentQuantity,
    decimal ReturnedQuantity, decimal ScrapQuantity, decimal OutstandingQuantity);

public sealed record TransportDocumentExportRow(
    int Number, int Year, DateTime IssuedAt, string Reason, string? CustomerCode, string RecipientName,
    string? RecipientVatNumber, string? WorkOrderCode, int LineNumber, string? Code, string Description,
    string Unit, decimal Quantity, string? LotNumber);
