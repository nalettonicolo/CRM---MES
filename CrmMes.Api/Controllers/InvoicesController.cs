using System.Security.Claims;
using System.Text;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Electronic invoices (see Invoice and FatturaPa). The usual flow is the deferred invoice: pick
/// a customer's issued DDTs not yet invoiced, get a draft with their lines and the prices already sold
/// (work order sale price or quote line), check VAT, issue, download the XML for the Exchange System.
/// Also manual invoices (TD01) for services or goods without a DDT.</summary>
[ApiController]
[Authorize]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private const int MaxNumberingAttempts = 3;
    private readonly ApplicationDbContext _dbContext;

    public InvoicesController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<InvoiceSummaryResponse>>> GetInvoices(
        [FromQuery] string? status = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Invoices.AsNoTracking().Include(i => i.Customer).Include(i => i.Lines).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(i => i.Status == status.Trim());
        }

        var invoices = await query.OrderByDescending(i => i.Year).ThenByDescending(i => i.Number).ThenByDescending(i => i.CreatedAt)
            .Take(300).ToListAsync(cancellationToken);
        return Ok(invoices.Select(i => new InvoiceSummaryResponse(
            i.Id, Code(i), i.Status, i.DocumentType, i.CustomerId, i.Customer.Name, i.IssueDate, FatturaPa.Total(i.Lines), i.CreatedAt)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceResponse>> GetInvoice(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(id, tracking: false, cancellationToken);
        return invoice is null ? NotFound() : Ok(await ToResponseAsync(invoice, cancellationToken));
    }

    /// <summary>Issued DDTs of a customer that no invoice covers yet.</summary>
    [HttpGet("uninvoiced-transport-documents")]
    public async Task<ActionResult<IEnumerable<UninvoicedDocumentResponse>>> GetUninvoiced(
        [FromQuery] Guid? customerId = null, CancellationToken cancellationToken = default)
    {
        var invoiced = _dbContext.InvoiceTransportDocuments.Select(t => t.TransportDocumentId);
        var query = _dbContext.TransportDocuments.AsNoTracking().Include(d => d.Customer)
            .Where(d => d.Status == "Issued" && d.CustomerId != null && !invoiced.Contains(d.Id));
        if (customerId.HasValue)
        {
            query = query.Where(d => d.CustomerId == customerId);
        }

        var documents = await query.OrderBy(d => d.IssuedAt).Take(500).ToListAsync(cancellationToken);
        return Ok(documents.Select(d => new UninvoicedDocumentResponse(
            d.Id, $"{d.Number}/{d.Year}", d.IssuedAt, d.CustomerId!.Value, d.Customer!.Name, TransportReasons.Label(d.Reason, d.ReasonDetail))));
    }

    /// <summary>Deferred invoice draft (TD24) from DDTs of one customer: one line per DDT line, priced
    /// from what was sold (see <see cref="SuggestPriceAsync"/>); lines without a known price start at 0
    /// and are listed in the warnings so they get priced before issuing.</summary>
    [Authorize(Policy = "Sales")]
    [HttpPost("from-transport-documents")]
    public async Task<ActionResult<InvoiceResponse>> CreateFromTransportDocuments(
        CreateInvoiceFromDocumentsRequest request, CancellationToken cancellationToken = default)
    {
        var ids = (request.TransportDocumentIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return BadRequest(new { message = "Scegli almeno un DDT da fatturare." });
        }

        var documents = await _dbContext.TransportDocuments.AsNoTracking()
            .Include(d => d.Lines)
            .Include(d => d.WorkOrder)
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(cancellationToken);
        if (documents.Count != ids.Count)
        {
            return BadRequest(new { message = "DDT non trovato." });
        }

        if (documents.Any(d => d.Status != "Issued"))
        {
            return BadRequest(new { message = "Si fatturano solo DDT emessi." });
        }

        var customerIds = documents.Select(d => d.CustomerId).Distinct().ToList();
        if (customerIds.Count != 1 || customerIds[0] is null)
        {
            return BadRequest(new { message = "I DDT devono essere tutti dello stesso cliente (in anagrafica)." });
        }

        if (await _dbContext.InvoiceTransportDocuments.AnyAsync(t => ids.Contains(t.TransportDocumentId), cancellationToken))
        {
            return Conflict(new { message = "Uno dei DDT è già fatturato." });
        }

        var invoice = new Invoice
        {
            DocumentType = "TD24",
            CustomerId = customerIds[0]!.Value,
            CreatedBy = User.FindFirstValue(ClaimTypes.Name),
            PaymentMethod = "MP05",
        };
        var lineNumber = 1;
        foreach (var document in documents.OrderBy(d => d.IssuedAt))
        {
            invoice.TransportDocuments.Add(new InvoiceTransportDocument { TransportDocumentId = document.Id });
            foreach (var line in document.Lines.OrderBy(l => l.LineNumber))
            {
                var (price, discount) = await SuggestPriceAsync(document, line, cancellationToken);
                invoice.Lines.Add(new InvoiceLine
                {
                    LineNumber = lineNumber++,
                    Code = line.Code,
                    Description = line.LotNumber is null ? line.Description : $"{line.Description} - lotto {line.LotNumber}",
                    Quantity = line.Quantity,
                    Unit = line.Unit,
                    UnitPrice = price,
                    DiscountPercent = discount,
                    VatRate = 22,
                    TransportDocumentId = document.Id
                });
            }
        }

        _dbContext.Invoices.Add(invoice);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Uno dei DDT è appena stato fatturato da un altro utente." });
        }

        return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, await ToResponseAsync((await LoadAsync(invoice.Id, false, cancellationToken))!, cancellationToken));
    }

    [Authorize(Policy = "Sales")]
    [HttpPost]
    public async Task<ActionResult<InvoiceResponse>> CreateManual(SaveInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CustomerId is not { } customerId || !await _dbContext.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            return BadRequest(new { message = "Scegli il cliente." });
        }

        var invoice = new Invoice { CustomerId = customerId, DocumentType = "TD01", CreatedBy = User.FindFirstValue(ClaimTypes.Name) };
        if (Apply(invoice, request) is { } error)
        {
            return error;
        }

        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, await ToResponseAsync((await LoadAsync(invoice.Id, false, cancellationToken))!, cancellationToken));
    }

    [Authorize(Policy = "Sales")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InvoiceResponse>> Update(Guid id, SaveInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(id, tracking: true, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Status != "Draft")
        {
            return Conflict(new { message = "Una fattura emessa non si modifica: si rettifica con una nota di credito." });
        }

        if (Apply(invoice, request) is { } error)
        {
            return error;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await ToResponseAsync((await LoadAsync(id, false, cancellationToken))!, cancellationToken));
    }

    [Authorize(Policy = "Sales")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Status != "Draft")
        {
            return Conflict(new { message = "Una fattura emessa non si elimina." });
        }

        _dbContext.Invoices.Remove(invoice);   // frees its DDTs for another invoice
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Checks everything the Exchange System would reject, then assigns the next number of the
    /// issue year and freezes the invoice.</summary>
    [Authorize(Policy = "Sales")]
    [HttpPost("{id:guid}/issue")]
    public async Task<ActionResult<InvoiceResponse>> Issue(Guid id, IssueInvoiceRequest? request, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var invoice = await LoadAsync(id, tracking: true, cancellationToken);
            if (invoice is null)
            {
                return NotFound();
            }

            if (invoice.Status != "Draft")
            {
                return Conflict(new { message = "Fattura già emessa." });
            }

            var company = await _dbContext.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            var fiscal = await _dbContext.CustomerFiscalData.AsNoTracking().FirstOrDefaultAsync(f => f.CustomerId == invoice.CustomerId, cancellationToken);
            var problems = FatturaPa.Validate(company, invoice.Customer, fiscal, invoice.Lines.ToList());
            if (problems.Count > 0)
            {
                return BadRequest(new { message = "Fattura non emettibile: " + string.Join(" ", problems), problems });
            }

            var issueDate = (request?.IssueDate ?? DateTime.UtcNow).Date;
            if (issueDate > DateTime.UtcNow.Date.AddDays(1))
            {
                return BadRequest(new { message = "La data della fattura non può essere futura." });
            }

            var last = await _dbContext.Invoices.Where(i => i.Year == issueDate.Year && i.Number != null).MaxAsync(i => i.Number, cancellationToken);
            invoice.Year = issueDate.Year;
            invoice.Number = (last ?? 0) + 1;
            invoice.IssueDate = DateTime.SpecifyKind(issueDate, DateTimeKind.Utc);
            invoice.Status = "Issued";
            invoice.IssuedAt = DateTime.UtcNow;
            invoice.IssuedBy = User.FindFirstValue(ClaimTypes.Name);
            _dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "InvoiceIssued",
                EntityType = "Invoice",
                EntityId = invoice.Id,
                UserName = invoice.IssuedBy,
                Details = $"Fattura {invoice.Number}/{invoice.Year} ({invoice.DocumentType}) a {invoice.Customer.Name}, totale {FatturaPa.Total(invoice.Lines):0.00} EUR."
            });
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Ok(await ToResponseAsync((await LoadAsync(id, false, cancellationToken))!, cancellationToken));
            }
            catch (DbUpdateException) when (attempt < MaxNumberingAttempts)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>The FatturaPA XML of an issued invoice, named as the Exchange System expects.</summary>
    [HttpGet("{id:guid}/xml")]
    public async Task<IActionResult> GetXml(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(id, tracking: false, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Status != "Issued")
        {
            return Conflict(new { message = "Il file XML si genera dalla fattura emessa." });
        }

        var company = await _dbContext.CompanyProfiles.AsNoTracking().FirstAsync(cancellationToken);
        var fiscal = await _dbContext.CustomerFiscalData.AsNoTracking().FirstAsync(f => f.CustomerId == invoice.CustomerId, cancellationToken);
        var documentIds = invoice.TransportDocuments.Select(t => t.TransportDocumentId).ToList();
        var documents = await _dbContext.TransportDocuments.AsNoTracking().Where(d => documentIds.Contains(d.Id)).ToListAsync(cancellationToken);
        var xml = FatturaPa.BuildXml(invoice, company, invoice.Customer, fiscal, documents);
        return File(Encoding.UTF8.GetBytes(xml), "application/xml", FatturaPa.FileName(company, invoice));
    }

    private async Task<(decimal Price, decimal Discount)> SuggestPriceAsync(TransportDocument document, TransportDocumentLine line, CancellationToken cancellationToken)
    {
        if (line.ProductId is null || document.WorkOrder is not { } order)
        {
            return (0, 0);
        }

        if (order.QuoteId is { } quoteId)
        {
            var item = await _dbContext.QuoteItems.AsNoTracking()
                .Where(i => i.QuoteId == quoteId && i.ProductId == line.ProductId)
                .OrderBy(i => i.SequenceNumber)
                .FirstOrDefaultAsync(cancellationToken);
            if (item is not null)
            {
                return (item.UnitPrice, item.DiscountPercent);
            }
        }

        return order.SalePrice is { } sale && order.Quantity > 0 ? (Math.Round(sale / order.Quantity, 4), 0) : (0, 0);
    }

    private ActionResult? Apply(Invoice invoice, SaveInvoiceRequest request)
    {
        if (request.PaymentMethod is { } method && !FatturaPa.PaymentMethods.ContainsKey(method))
        {
            return BadRequest(new { message = "Modalità di pagamento non valida." });
        }

        var lines = request.Lines ?? [];
        if (lines.Count == 0)
        {
            return BadRequest(new { message = "Aggiungi almeno una riga." });
        }

        var allowedDocuments = invoice.TransportDocuments.Select(t => t.TransportDocumentId).ToHashSet();
        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            if (string.IsNullOrWhiteSpace(line.Description) || line.Quantity <= 0 || line.UnitPrice < 0 || line.DiscountPercent is < 0 or > 100)
            {
                return BadRequest(new { message = $"Riga {index + 1}: descrizione, quantità positiva, prezzo e sconto validi." });
            }

            if (!FatturaPa.VatRates.Contains(line.VatRate))
            {
                return BadRequest(new { message = $"Riga {index + 1}: aliquota IVA non ammessa." });
            }

            if (line.VatRate == 0 && (line.VatNature is null || !FatturaPa.Natures.ContainsKey(line.VatNature)))
            {
                return BadRequest(new { message = $"Riga {index + 1}: con IVA 0% indica la natura (es. N3.1, N6.7)." });
            }

            if (line.TransportDocumentId is { } documentId && !allowedDocuments.Contains(documentId))
            {
                return BadRequest(new { message = $"Riga {index + 1}: il DDT collegato non è di questa fattura." });
            }
        }

        invoice.PaymentMethod = request.PaymentMethod ?? invoice.PaymentMethod;
        invoice.PaymentDueDate = request.PaymentDueDate?.Date;
        invoice.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        var tracked = _dbContext.Entry(invoice).State is not (EntityState.Detached or EntityState.Added);
        _dbContext.InvoiceLines.RemoveRange(invoice.Lines);
        invoice.Lines.Clear();
        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            var entity = new InvoiceLine
            {
                InvoiceId = invoice.Id,
                LineNumber = index + 1,
                Code = string.IsNullOrWhiteSpace(line.Code) ? null : line.Code.Trim(),
                Description = line.Description!.Trim(),
                Quantity = line.Quantity,
                Unit = string.IsNullOrWhiteSpace(line.Unit) ? "pz" : line.Unit.Trim(),
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                VatRate = line.VatRate,
                VatNature = line.VatRate == 0 ? line.VatNature : null,
                TransportDocumentId = line.TransportDocumentId
            };
            invoice.Lines.Add(entity);
            if (tracked)
            {
                _dbContext.Entry(entity).State = EntityState.Added;
            }
        }

        return null;
    }

    private Task<Invoice?> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = _dbContext.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Lines)
            .Include(i => i.TransportDocuments)
            .AsSplitQuery();
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    private async Task<InvoiceResponse> ToResponseAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var documentIds = invoice.TransportDocuments.Select(t => t.TransportDocumentId).ToList();
        var documents = await _dbContext.TransportDocuments.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Number, d.Year, d.IssuedAt })
            .ToListAsync(cancellationToken);
        var lines = invoice.Lines.OrderBy(l => l.LineNumber).ToList();
        var warnings = new List<string>();
        var unpriced = lines.Count(l => l.UnitPrice == 0);
        if (unpriced > 0 && invoice.Status == "Draft")
        {
            warnings.Add($"{unpriced} righe senza prezzo: indicalo prima di emettere.");
        }

        if (invoice.Status == "Draft" && invoice.DocumentType == "TD24" && documents.Count > 0)
        {
            var latest = documents.Max(d => d.IssuedAt) ?? DateTime.UtcNow;
            var deadline = new DateTime(latest.Year, latest.Month, 15).AddMonths(1);
            warnings.Add($"Fattura differita: va emessa entro il {deadline:dd/MM/yyyy} (15 del mese successivo al DDT).");
        }

        return new InvoiceResponse(
            invoice.Id, Code(invoice), invoice.Number, invoice.Year, invoice.Status, invoice.DocumentType,
            invoice.CustomerId, invoice.Customer.Name, invoice.IssueDate, invoice.PaymentMethod, invoice.PaymentDueDate, invoice.Notes,
            lines.Select(l => new InvoiceLineResponse(l.Id, l.LineNumber, l.Code, l.Description, l.Quantity, l.Unit, l.UnitPrice,
                l.DiscountPercent, l.VatRate, l.VatNature, FatturaPa.LineTotal(l), l.TransportDocumentId)).ToList(),
            documents.OrderBy(d => d.IssuedAt).Select(d => new InvoiceDocumentResponse(d.Id, $"{d.Number}/{d.Year}", d.IssuedAt)).ToList(),
            FatturaPa.Summaries(lines).Select(s => new InvoiceSummaryLineResponse(s.Rate, s.Nature, s.Taxable, s.Tax)).ToList(),
            FatturaPa.Total(lines), warnings, invoice.IssuedAt, invoice.IssuedBy);
    }

    private static string Code(Invoice invoice) => invoice.Number is { } n ? $"{n}/{invoice.Year}" : "Bozza";
}

[ApiController]
[Authorize]
public class FiscalDataController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public FiscalDataController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("api/customers/{customerId:guid}/fiscal")]
    public async Task<ActionResult<CustomerFiscalResponse>> GetCustomer(Guid customerId, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            return NotFound();
        }

        var data = await _dbContext.CustomerFiscalData.AsNoTracking().FirstOrDefaultAsync(f => f.CustomerId == customerId, cancellationToken)
                   ?? new CustomerFiscalData { CustomerId = customerId };
        return Ok(new CustomerFiscalResponse(data.FiscalCode, data.SdiCode, data.Pec, data.Street, data.PostalCode, data.City, data.Province, data.Country));
    }

    [Authorize(Policy = "Sales")]
    [HttpPut("api/customers/{customerId:guid}/fiscal")]
    public async Task<ActionResult<CustomerFiscalResponse>> SaveCustomer(Guid customerId, CustomerFiscalResponse request, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            return NotFound();
        }

        var country = (Clean(request.Country) ?? "IT").ToUpperInvariant();
        if (country.Length != 2 || !country.All(char.IsAsciiLetter))
        {
            return BadRequest(new { message = "Nazione: codice di 2 lettere (IT, DE, FR...)." });
        }

        if (request.SdiCode is { Length: > 0 } sdi && !(sdi.Trim().Length is 6 or 7 && sdi.Trim().All(char.IsAsciiLetterOrDigit)))
        {
            return BadRequest(new { message = "Codice destinatario SDI: 7 caratteri." });
        }

        if (country == "IT" && request.PostalCode is { Length: > 0 } cap && !(cap.Trim().Length == 5 && cap.Trim().All(char.IsAsciiDigit)))
        {
            return BadRequest(new { message = "CAP: 5 cifre." });
        }

        if (request.Province is { Length: > 0 } province && !(province.Trim().Length == 2 && province.Trim().All(char.IsAsciiLetter)))
        {
            return BadRequest(new { message = "Provincia: sigla di 2 lettere." });
        }

        var data = await _dbContext.CustomerFiscalData.FirstOrDefaultAsync(f => f.CustomerId == customerId, cancellationToken);
        if (data is null)
        {
            data = new CustomerFiscalData { CustomerId = customerId };
            _dbContext.CustomerFiscalData.Add(data);
        }

        data.FiscalCode = Clean(request.FiscalCode)?.ToUpperInvariant();
        data.SdiCode = Clean(request.SdiCode)?.ToUpperInvariant();
        data.Pec = Clean(request.Pec);
        data.Street = Clean(request.Street);
        data.PostalCode = Clean(request.PostalCode);
        data.City = Clean(request.City);
        data.Province = Clean(request.Province)?.ToUpperInvariant();
        data.Country = country;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new CustomerFiscalResponse(data.FiscalCode, data.SdiCode, data.Pec, data.Street, data.PostalCode, data.City, data.Province, data.Country));
    }

    [HttpGet("api/company-profile/fiscal")]
    public async Task<ActionResult<CompanyFiscalResponse>> GetCompany(CancellationToken cancellationToken = default)
    {
        var company = await _dbContext.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return company is null
            ? Ok(new CompanyFiscalResponse(null, "RF01", null, null, null, null, "IT", null, null, null))
            : Ok(new CompanyFiscalResponse(company.FiscalCode, company.TaxRegime, company.Street, company.PostalCode, company.City,
                company.Province, company.Country, company.ReaOffice, company.ReaNumber, company.Iban));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("api/company-profile/fiscal")]
    public async Task<ActionResult<CompanyFiscalResponse>> SaveCompany(CompanyFiscalResponse request, CancellationToken cancellationToken = default)
    {
        var company = await _dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return Conflict(new { message = "Configura prima i dati dell'azienda." });
        }

        var regime = Clean(request.TaxRegime) ?? "RF01";
        if (!(regime.Length == 4 && regime.StartsWith("RF") && int.TryParse(regime[2..], out var code) && code is >= 1 and <= 19 && code != 3))
        {
            return BadRequest(new { message = "Regime fiscale non valido (RF01 ordinario, RF19 forfettario...)." });
        }

        if (request.PostalCode is { Length: > 0 } cap && !(cap.Trim().Length == 5 && cap.Trim().All(char.IsAsciiDigit)))
        {
            return BadRequest(new { message = "CAP: 5 cifre." });
        }

        foreach (var province in new[] { request.Province, request.ReaOffice })
        {
            if (province is { Length: > 0 } && !(province.Trim().Length == 2 && province.Trim().All(char.IsAsciiLetter)))
            {
                return BadRequest(new { message = "Provincia: sigla di 2 lettere." });
            }
        }

        var iban = Clean(request.Iban)?.Replace(" ", string.Empty).ToUpperInvariant();
        if (iban is not null && !IsValidIban(iban))
        {
            return BadRequest(new { message = "IBAN non valido." });
        }

        company.FiscalCode = Clean(request.FiscalCode)?.ToUpperInvariant();
        company.TaxRegime = regime;
        company.Street = Clean(request.Street);
        company.PostalCode = Clean(request.PostalCode);
        company.City = Clean(request.City);
        company.Province = Clean(request.Province)?.ToUpperInvariant();
        company.Country = "IT";
        company.ReaOffice = Clean(request.ReaOffice)?.ToUpperInvariant();
        company.ReaNumber = Clean(request.ReaNumber);
        company.Iban = iban;
        company.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new CompanyFiscalResponse(company.FiscalCode, company.TaxRegime, company.Street, company.PostalCode, company.City,
            company.Province, company.Country, company.ReaOffice, company.ReaNumber, company.Iban));
    }

    /// <summary>ISO 13616 check: country, check digits, then mod 97 of the rearranged number equals 1.</summary>
    public static bool IsValidIban(string iban)
    {
        if (iban.Length is < 15 or > 34 || !iban.All(char.IsAsciiLetterOrDigit) || !char.IsAsciiLetter(iban[0]) || !char.IsAsciiLetter(iban[1]))
        {
            return false;
        }

        var rearranged = iban[4..] + iban[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            var value = char.IsAsciiDigit(c) ? c - '0' : char.ToUpperInvariant(c) - 'A' + 10;
            remainder = value >= 10 ? (remainder * 100 + value) % 97 : (remainder * 10 + value) % 97;
        }

        return remainder == 1;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CreateInvoiceFromDocumentsRequest(List<Guid>? TransportDocumentIds);

public sealed record InvoiceLineRequest(
    string? Code, string? Description, decimal Quantity, string? Unit, decimal UnitPrice, decimal DiscountPercent,
    decimal VatRate, string? VatNature, Guid? TransportDocumentId);

public sealed record SaveInvoiceRequest(
    Guid? CustomerId, string? PaymentMethod, DateTime? PaymentDueDate, string? Notes, List<InvoiceLineRequest>? Lines);

public sealed record IssueInvoiceRequest(DateTime? IssueDate);

public sealed record InvoiceSummaryResponse(
    Guid Id, string Code, string Status, string DocumentType, Guid CustomerId, string CustomerName, DateTime? IssueDate,
    decimal Total, DateTime CreatedAt);

public sealed record InvoiceLineResponse(
    Guid Id, int LineNumber, string? Code, string Description, decimal Quantity, string Unit, decimal UnitPrice,
    decimal DiscountPercent, decimal VatRate, string? VatNature, decimal LineTotal, Guid? TransportDocumentId);

public sealed record InvoiceDocumentResponse(Guid Id, string DocumentCode, DateTime? IssuedAt);

public sealed record InvoiceSummaryLineResponse(decimal Rate, string? Nature, decimal Taxable, decimal Tax);

public sealed record InvoiceResponse(
    Guid Id, string Code, int? Number, int? Year, string Status, string DocumentType, Guid CustomerId, string CustomerName,
    DateTime? IssueDate, string PaymentMethod, DateTime? PaymentDueDate, string? Notes, List<InvoiceLineResponse> Lines,
    List<InvoiceDocumentResponse> TransportDocuments, List<InvoiceSummaryLineResponse> VatSummary, decimal Total,
    List<string> Warnings, DateTime? IssuedAt, string? IssuedBy);

public sealed record UninvoicedDocumentResponse(Guid Id, string DocumentCode, DateTime? IssuedAt, Guid CustomerId, string CustomerName, string Reason);

public sealed record CustomerFiscalResponse(
    string? FiscalCode, string? SdiCode, string? Pec, string? Street, string? PostalCode, string? City, string? Province, string? Country);

public sealed record CompanyFiscalResponse(
    string? FiscalCode, string? TaxRegime, string? Street, string? PostalCode, string? City, string? Province, string? Country,
    string? ReaOffice, string? ReaNumber, string? Iban);
