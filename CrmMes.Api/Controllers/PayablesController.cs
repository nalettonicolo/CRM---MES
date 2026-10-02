using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Passive FatturaPA invoices (received from suppliers) and the company payment schedule:
/// amounts to pay and to collect, with mark-paid and reminder stamps.</summary>
[ApiController]
[Authorize]
[Route("api/payables")]
public class PayablesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public PayablesController(ApplicationDbContext db) => _db = db;

    [HttpGet("purchase-invoices")]
    [Authorize(Policy = "Purchasing")]
    public async Task<ActionResult<List<PurchaseInvoiceSummaryResponse>>> ListPurchaseInvoices(
        CancellationToken cancellationToken)
    {
        var rows = await _db.PurchaseInvoices.AsNoTracking()
            .OrderByDescending(i => i.DocumentDate).ThenByDescending(i => i.ImportedAt)
            .Select(i => new PurchaseInvoiceSummaryResponse(
                i.Id,
                i.DocumentNumber,
                i.DocumentDate,
                i.DocumentType,
                i.SupplierName,
                i.SupplierVat,
                i.SupplierId,
                i.Lines.Sum(l => l.Quantity * l.UnitPrice * (1 - l.DiscountPercent / 100m)),
                i.ImportedAt,
                i.Schedule.Count(s => s.Status == "Open"),
                i.Schedule.Count(s => s.Status == "Paid")))
            .Take(200)
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }

    [HttpGet("purchase-invoices/{id:guid}")]
    [Authorize(Policy = "Purchasing")]
    public async Task<ActionResult<PurchaseInvoiceDetailResponse>> GetPurchaseInvoice(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await _db.PurchaseInvoices.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Schedule)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        return Ok(ToDetail(invoice));
    }

    [HttpPost("purchase-invoices/import")]
    [Authorize(Policy = "Purchasing")]
    [RequestSizeLimit(5_000_000)]
    public async Task<ActionResult<PurchaseInvoiceDetailResponse>> Import(
        IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Seleziona un file XML FatturaPA non vuoto." });
        }

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        FatturaPaImport.ParsedInvoice parsed;
        try
        {
            parsed = FatturaPaImport.Parse(bytes);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }

        if (await _db.PurchaseInvoices.AnyAsync(i => i.ContentHash == parsed.ContentHash, cancellationToken))
        {
            return Conflict(new { message = "Questo file XML è già stato importato." });
        }

        if (await _db.PurchaseInvoices.AnyAsync(
                i => i.DocumentNumber == parsed.DocumentNumber
                     && i.DocumentDate == parsed.DocumentDate
                     && i.SupplierVat == parsed.SupplierVat,
                cancellationToken))
        {
            return Conflict(new { message = $"La fattura {parsed.DocumentNumber} del {parsed.DocumentDate:dd/MM/yyyy} risulta già importata." });
        }

        var supplier = await ResolveSupplierAsync(parsed, cancellationToken);
        var invoice = new PurchaseInvoice
        {
            DocumentNumber = parsed.DocumentNumber,
            DocumentDate = parsed.DocumentDate,
            DocumentType = parsed.DocumentType,
            SupplierId = supplier?.Id,
            SupplierName = parsed.SupplierName,
            SupplierVat = parsed.SupplierVat,
            PaymentMethod = parsed.PaymentMethod,
            Currency = parsed.Currency,
            ContentHash = parsed.ContentHash,
            OriginalFileName = file.FileName,
            ImportedBy = User.FindFirstValue(ClaimTypes.Name),
            Lines = parsed.Lines.Select(line => new PurchaseInvoiceLine
            {
                LineNumber = line.LineNumber,
                Code = line.Code,
                Description = line.Description,
                Quantity = line.Quantity,
                Unit = line.Unit,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                VatRate = line.VatRate,
                VatNature = line.VatNature,
            }).ToList(),
        };

        foreach (var payment in parsed.Payments)
        {
            invoice.Schedule.Add(new PaymentScheduleEntry
            {
                Direction = "Payable",
                Status = "Open",
                DueDate = payment.DueDate,
                Amount = payment.Amount,
                Currency = parsed.Currency,
                CounterpartyName = parsed.SupplierName,
                Description = $"Fattura {parsed.DocumentNumber}",
            });
        }

        _db.PurchaseInvoices.Add(invoice);
        _db.AuditLogs.Add(new AuditLog
        {
            Action = "PurchaseInvoiceImported",
            EntityType = nameof(PurchaseInvoice),
            EntityId = invoice.Id,
            Details = $"{parsed.SupplierName} · {parsed.DocumentNumber} · {parsed.Payments.Count} scadenze",
            UserName = User.FindFirstValue(ClaimTypes.Name),
        });
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetPurchaseInvoice), new { id = invoice.Id }, ToDetail(invoice));
    }

    [HttpGet("schedule")]
    [Authorize(Policy = "PurchasingOrSales")]
    public async Task<ActionResult<List<ScheduleEntryResponse>>> Schedule(
        [FromQuery] string? direction = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReceivableEntriesAsync(cancellationToken);

        var query = _db.PaymentScheduleEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(direction))
        {
            query = query.Where(e => e.Direction == direction);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(e => e.Status == status);
        }

        var today = DateTime.UtcNow.Date;
        var rows = await query
            .OrderBy(e => e.Status == "Open" ? 0 : 1)
            .ThenBy(e => e.DueDate)
            .Take(500)
            .Select(e => new ScheduleEntryResponse(
                e.Id,
                e.Direction,
                e.Status,
                e.DueDate,
                e.Amount,
                e.Currency,
                e.CounterpartyName,
                e.Description,
                e.PurchaseInvoiceId,
                e.InvoiceId,
                e.PaidAt,
                e.RemindedAt,
                e.ReminderCount,
                e.Status == "Open" && e.DueDate.Date < today))
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }

    [HttpPost("schedule/{id:guid}/mark-paid")]
    [Authorize(Policy = "PurchasingOrSales")]
    public async Task<ActionResult<ScheduleEntryResponse>> MarkPaid(Guid id, CancellationToken cancellationToken)
    {
        var entry = await _db.PaymentScheduleEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        if (entry.Status == "Paid")
        {
            return BadRequest(new { message = "Questa scadenza è già segnata come pagata." });
        }

        entry.Status = "Paid";
        entry.PaidAt = DateTime.UtcNow;
        entry.PaidBy = User.FindFirstValue(ClaimTypes.Name);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToScheduleResponse(entry));
    }

    [HttpPost("schedule/{id:guid}/remind")]
    [Authorize(Policy = "PurchasingOrSales")]
    public async Task<ActionResult<ScheduleEntryResponse>> Remind(
        Guid id, [FromBody] RemindRequest? request, CancellationToken cancellationToken)
    {
        var entry = await _db.PaymentScheduleEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        if (entry.Status != "Open")
        {
            return BadRequest(new { message = "Si possono sollecitare solo scadenze aperte." });
        }

        entry.RemindedAt = DateTime.UtcNow;
        entry.ReminderCount += 1;
        entry.ReminderNote = string.IsNullOrWhiteSpace(request?.Note) ? entry.ReminderNote : request.Note.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToScheduleResponse(entry));
    }

    private async Task<Supplier?> ResolveSupplierAsync(FatturaPaImport.ParsedInvoice parsed, CancellationToken cancellationToken)
    {
        var vatDigits = FatturaPa.VatDigits(parsed.SupplierVat);
        if (!string.IsNullOrWhiteSpace(vatDigits))
        {
            var byCode = await _db.Suppliers.FirstOrDefaultAsync(
                s => s.Code == vatDigits || s.Code == parsed.SupplierVat, cancellationToken);
            if (byCode is not null)
            {
                return byCode;
            }
        }

        var byName = await _db.Suppliers.FirstOrDefaultAsync(
            s => s.Name == parsed.SupplierName, cancellationToken);
        if (byName is not null)
        {
            return byName;
        }

        var code = string.IsNullOrWhiteSpace(vatDigits) ? $"F-{Guid.NewGuid():N}"[..10] : vatDigits;
        if (await _db.Suppliers.AnyAsync(s => s.Code == code, cancellationToken))
        {
            code = $"{code}-{Guid.NewGuid():N}"[..12];
        }

        var created = new Supplier
        {
            Name = parsed.SupplierName,
            Code = code,
            IsActive = true,
        };
        _db.Suppliers.Add(created);
        return created;
    }

    /// <summary>Issued sales invoices with a due date feed the receivable side of the schedule.</summary>
    private async Task EnsureReceivableEntriesAsync(CancellationToken cancellationToken)
    {
        var issued = await _db.Invoices.AsNoTracking()
            .Include(i => i.Customer)
            .Include(i => i.Lines)
            .Where(i => i.Status == "Issued" && i.PaymentDueDate != null)
            .ToListAsync(cancellationToken);
        if (issued.Count == 0)
        {
            return;
        }

        var existing = await _db.PaymentScheduleEntries
            .Where(e => e.InvoiceId != null)
            .Select(e => e.InvoiceId!.Value)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();
        var added = false;
        foreach (var invoice in issued.Where(i => !existingSet.Contains(i.Id)))
        {
            var amount = FatturaPa.Summaries(invoice.Lines).Sum(s => s.Taxable + s.Tax);
            _db.PaymentScheduleEntries.Add(new PaymentScheduleEntry
            {
                Direction = "Receivable",
                Status = "Open",
                DueDate = DateTime.SpecifyKind(invoice.PaymentDueDate!.Value.Date, DateTimeKind.Utc),
                Amount = amount,
                Currency = "EUR",
                CounterpartyName = invoice.Customer.Name,
                Description = invoice.Number is { } n && invoice.Year is { } y
                    ? $"Fattura {n}/{y}"
                    : "Fattura emessa",
                InvoiceId = invoice.Id,
            });
            added = true;
        }

        if (added)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static PurchaseInvoiceDetailResponse ToDetail(PurchaseInvoice invoice) => new(
        invoice.Id,
        invoice.DocumentNumber,
        invoice.DocumentDate,
        invoice.DocumentType,
        invoice.SupplierName,
        invoice.SupplierVat,
        invoice.SupplierId,
        invoice.PaymentMethod,
        invoice.Currency,
        invoice.OriginalFileName,
        invoice.ImportedAt,
        invoice.ImportedBy,
        invoice.Lines.OrderBy(l => l.LineNumber).Select(l => new PurchaseInvoiceLineResponse(
            l.LineNumber, l.Code, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.DiscountPercent, l.VatRate, l.VatNature)).ToList(),
        invoice.Schedule.OrderBy(s => s.DueDate).Select(ToScheduleResponse).ToList());

    private static ScheduleEntryResponse ToScheduleResponse(PaymentScheduleEntry e) => new(
        e.Id,
        e.Direction,
        e.Status,
        e.DueDate,
        e.Amount,
        e.Currency,
        e.CounterpartyName,
        e.Description,
        e.PurchaseInvoiceId,
        e.InvoiceId,
        e.PaidAt,
        e.RemindedAt,
        e.ReminderCount,
        e.Status == "Open" && e.DueDate.Date < DateTime.UtcNow.Date);

    public sealed record RemindRequest(string? Note);
}

public sealed record PurchaseInvoiceSummaryResponse(
    Guid Id, string DocumentNumber, DateTime DocumentDate, string DocumentType,
    string SupplierName, string? SupplierVat, Guid? SupplierId, decimal TaxableTotal,
    DateTime ImportedAt, int OpenInstallments, int PaidInstallments);

public sealed record PurchaseInvoiceLineResponse(
    int LineNumber, string? Code, string Description, decimal Quantity, string Unit,
    decimal UnitPrice, decimal DiscountPercent, decimal VatRate, string? VatNature);

public sealed record PurchaseInvoiceDetailResponse(
    Guid Id, string DocumentNumber, DateTime DocumentDate, string DocumentType,
    string SupplierName, string? SupplierVat, Guid? SupplierId, string? PaymentMethod,
    string? Currency, string? OriginalFileName, DateTime ImportedAt, string? ImportedBy,
    IReadOnlyList<PurchaseInvoiceLineResponse> Lines, IReadOnlyList<ScheduleEntryResponse> Schedule);

public sealed record ScheduleEntryResponse(
    Guid Id, string Direction, string Status, DateTime DueDate, decimal Amount, string Currency,
    string? CounterpartyName, string? Description, Guid? PurchaseInvoiceId, Guid? InvoiceId,
    DateTime? PaidAt, DateTime? RemindedAt, int ReminderCount, bool Overdue);
