using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Customer quotes (preventivi): Draft -> Sent -> Accepted | Rejected, and one conversion of
/// an accepted quote into work orders — one per product line — so a job starts from what was actually
/// sold instead of being retyped by hand.</summary>
[ApiController]
[Authorize]
[Route("api/quotes")]
public class QuotesController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly WorkOrderFactory _workOrderFactory;

    public QuotesController(ApplicationDbContext dbContext, WorkOrderFactory workOrderFactory)
    {
        _dbContext = dbContext;
        _workOrderFactory = workOrderFactory;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<QuoteSummaryResponse>>> GetQuotes(
        [FromQuery] string? status = null,
        [FromQuery] Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Quotes.AsNoTracking().Include(quote => quote.Customer).Include(quote => quote.Items).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(quote => quote.Status == status.Trim());
        }

        if (customerId.HasValue)
        {
            query = query.Where(quote => quote.CustomerId == customerId);
        }

        // Totals computed in memory: Sqlite (test provider) can't aggregate decimals server-side.
        var quotes = (await query.OrderByDescending(quote => quote.CreatedAt).Take(200).ToListAsync(cancellationToken))
            .Select(quote => new QuoteSummaryResponse(
                quote.Id, quote.Code, quote.CustomerId, quote.Customer.Name, quote.Status,
                quote.CreatedAt, quote.ValidUntil, quote.ConvertedAt, quote.Items.Count, QuotePricing.Total(quote.Items)))
            .ToList();

        return Ok(quotes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuoteResponse>> GetQuote(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await LoadQuoteAsync(id, tracking: false, cancellationToken);
        return quote is null ? NotFound() : Ok(ToResponse(quote));
    }

    [Authorize(Policy = "Sales")]
    [HttpPost]
    public async Task<ActionResult<QuoteResponse>> CreateQuote(SaveQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var quote = new Quote
        {
            // Random suffix avoids collisions when two quotes are created within the same second.
            Code = $"PV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
            CustomerId = request.CustomerId,
            ValidUntil = request.ValidUntil,
            Notes = Clean(request.Notes),
            Status = "Draft"
        };

        var sequence = 1;
        foreach (var item in request.Items!)
        {
            quote.Items.Add(ToEntity(quote.Id, sequence++, item));
        }

        _dbContext.Quotes.Add(quote);
        AddAudit("QuoteCreated", quote, $"Preventivo {quote.Code} creato con {quote.Items.Count} righe.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        var created = await LoadQuoteAsync(quote.Id, tracking: false, cancellationToken);
        return CreatedAtAction(nameof(GetQuote), new { id = quote.Id }, ToResponse(created!));
    }

    /// <summary>Replaces header and lines of a Draft quote. Once sent, a quote is a document the customer
    /// holds: changing it means making a new one.</summary>
    [Authorize(Policy = "Sales")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<QuoteResponse>> EditQuote(Guid id, SaveQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var quote = await LoadQuoteAsync(id, tracking: true, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        if (quote.Status != "Draft")
        {
            return Conflict(new { message = "Solo un preventivo in bozza può essere modificato." });
        }

        var validation = await ValidateAsync(request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        quote.CustomerId = request.CustomerId;
        quote.ValidUntil = request.ValidUntil;
        quote.Notes = Clean(request.Notes);

        foreach (var item in quote.Items.ToList())
        {
            quote.Items.Remove(item);
        }

        var sequence = 1;
        foreach (var item in request.Items!)
        {
            // Adding to the tracked DbSet is enough: EF fixes up quote.Items since the parent is tracked
            // with the collection loaded (same pattern as ProductsController.ReplaceBillOfMaterial).
            _dbContext.QuoteItems.Add(ToEntity(quote.Id, sequence++, item));
        }

        AddAudit("QuoteEdited", quote, $"Preventivo {quote.Code} modificato.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        var saved = await LoadQuoteAsync(id, tracking: false, cancellationToken);
        return Ok(ToResponse(saved!));
    }

    [Authorize(Policy = "Sales")]
    [HttpPost("{id:guid}/send")]
    public Task<ActionResult<QuoteResponse>> SendQuote(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, ["Draft"], "Sent", quote => quote.SentAt = DateTime.UtcNow, "QuoteSent", "inviato al cliente", cancellationToken);

    [Authorize(Policy = "Sales")]
    [HttpPost("{id:guid}/accept")]
    public Task<ActionResult<QuoteResponse>> AcceptQuote(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, ["Draft", "Sent"], "Accepted", quote => quote.AcceptedAt = DateTime.UtcNow, "QuoteAccepted", "accettato", cancellationToken);

    [Authorize(Policy = "Sales")]
    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<QuoteResponse>> RejectQuote(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, ["Draft", "Sent"], "Rejected", quote => quote.RejectedAt = DateTime.UtcNow, "QuoteRejected", "rifiutato", cancellationToken);

    /// <summary>Turns an accepted quote into Draft work orders, one per product line, linked back to the
    /// quote and its customer. Free-text lines (installation, transport...) are priced but not produced,
    /// so they are skipped. A quote converts once: a second call is refused.</summary>
    [Authorize(Policy = "SalesOrWarehouse")]
    [HttpPost("{id:guid}/convert")]
    public async Task<ActionResult<ConvertQuoteResponse>> ConvertQuote(
        Guid id, ConvertQuoteRequest? request, CancellationToken cancellationToken = default)
    {
        var quote = await _dbContext.Quotes
            .Include(q => q.Customer)
            .Include(q => q.Items)
            .SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        if (quote.Status != "Accepted")
        {
            return Conflict(new { message = "Solo un preventivo accettato può diventare commessa." });
        }

        if (quote.ConvertedAt.HasValue)
        {
            return Conflict(new { message = "Questo preventivo è già stato convertito in commessa." });
        }

        var productLines = quote.Items.Where(item => item.ProductId.HasValue).OrderBy(item => item.SequenceNumber).ToList();
        if (productLines.Count == 0)
        {
            return BadRequest(new { message = "Il preventivo non contiene righe collegate a un prodotto: non c'è nulla da produrre." });
        }

        if (request?.AreaId is { } areaId &&
            !await _dbContext.Areas.AnyAsync(area => area.Id == areaId && area.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Area non trovata o non attiva." });
        }

        var productIds = productLines.Select(item => item.ProductId!.Value).Distinct().ToList();
        var products = await _dbContext.Products
            .Include(p => p.RoutingSteps)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var inactive = productLines
            .Where(item => !products.TryGetValue(item.ProductId!.Value, out var product) || !product.IsActive)
            .Select(item => item.Description)
            .ToList();
        if (inactive.Count > 0)
        {
            return BadRequest(new { message = "Alcuni prodotti del preventivo non esistono più o non sono attivi.", items = inactive });
        }

        var created = new List<ConvertedWorkOrderResponse>();
        foreach (var line in productLines)
        {
            var product = products[line.ProductId!.Value];
            var order = _workOrderFactory.Build(
                product,
                line.Quantity,
                areaId: request?.AreaId,
                customerReference: $"{quote.Customer.Name} · {quote.Code}",
                customerId: quote.CustomerId,
                quoteId: quote.Id,
                dueDate: request?.DueDate,
                notes: line.Description);

            _dbContext.WorkOrders.Add(order);
            created.Add(new ConvertedWorkOrderResponse(order.Id, order.Code, product.Code, order.Quantity));
        }

        quote.ConvertedAt = DateTime.UtcNow;
        AddAudit("QuoteConverted", quote,
            $"Preventivo {quote.Code} convertito in {created.Count} commesse: {string.Join(", ", created.Select(c => c.Code))}.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new ConvertQuoteResponse(quote.Id, quote.Code, created));
    }

    /// <summary>Material cost of one unit of a product, from its bill of materials and the cheapest
    /// catalog price known for each material. The quote editor uses it to suggest a line price; lines
    /// with no known price are counted separately, so an incomplete estimate is never presented as
    /// complete.</summary>
    [HttpGet("/api/products/{productId:guid}/material-cost")]
    public async Task<ActionResult<ProductMaterialCostResponse>> GetProductMaterialCost(
        Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products.AsNoTracking()
            .Include(p => p.BillOfMaterial)
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var codes = product.BillOfMaterial.Select(item => item.MaterialCode).Distinct().ToList();
        var prices = (await _dbContext.MaterialSuppliers.AsNoTracking()
                .Where(link => codes.Contains(link.Material.Code) && link.UnitPrice > 0)
                .Select(link => new { link.Material.Code, link.UnitPrice })
                .ToListAsync(cancellationToken))
            .GroupBy(entry => entry.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(entry => entry.UnitPrice), StringComparer.OrdinalIgnoreCase);

        var lines = product.BillOfMaterial
            .OrderBy(item => item.MaterialCode)
            .Select(item =>
            {
                decimal? unitPrice = prices.TryGetValue(item.MaterialCode, out var price) ? price : null;
                return new MaterialCostLineResponse(
                    item.MaterialCode, item.Quantity, unitPrice,
                    unitPrice.HasValue ? Math.Round(item.Quantity * unitPrice.Value, 2, MidpointRounding.AwayFromZero) : null);
            })
            .ToList();

        return Ok(new ProductMaterialCostResponse(
            product.Id, product.Code,
            lines.Sum(line => line.LineCost ?? 0),
            lines.Count(line => !line.UnitPrice.HasValue),
            lines));
    }

    private async Task<ActionResult<QuoteResponse>> TransitionAsync(
        Guid id, string[] allowedFrom, string to, Action<Quote> stamp, string auditAction, string verb, CancellationToken cancellationToken)
    {
        var quote = await LoadQuoteAsync(id, tracking: true, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        if (!allowedFrom.Contains(quote.Status))
        {
            return Conflict(new { message = $"Un preventivo nello stato '{quote.Status}' non può essere {verb}." });
        }

        quote.Status = to;
        stamp(quote);
        AddAudit(auditAction, quote, $"Preventivo {quote.Code} {verb}.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(quote));
    }

    private async Task<ActionResult?> ValidateAsync(SaveQuoteRequest request, CancellationToken cancellationToken)
    {
        if (request.CustomerId == Guid.Empty ||
            !await _dbContext.Customers.AnyAsync(c => c.Id == request.CustomerId && c.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Cliente non trovato o non attivo." });
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new { message = "Il preventivo deve contenere almeno una riga." });
        }

        if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.Description) || item.Quantity <= 0 || item.UnitPrice < 0))
        {
            return BadRequest(new { message = "Ogni riga deve avere descrizione, quantità maggiore di zero e prezzo non negativo." });
        }

        if (request.Items.Any(item => item.DiscountPercent is < 0 or > 100))
        {
            return BadRequest(new { message = "Lo sconto di riga deve essere tra 0 e 100%." });
        }

        var productIds = request.Items.Where(item => item.ProductId.HasValue).Select(item => item.ProductId!.Value).Distinct().ToList();
        if (productIds.Count > 0)
        {
            var activeCount = await _dbContext.Products.CountAsync(p => productIds.Contains(p.Id) && p.IsActive, cancellationToken);
            if (activeCount != productIds.Count)
            {
                return BadRequest(new { message = "Uno o più prodotti indicati nelle righe non esistono o non sono attivi." });
            }
        }

        return null;
    }

    private Task<Quote?> LoadQuoteAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = _dbContext.Quotes
            .Include(q => q.Customer)
            .Include(q => q.Items).ThenInclude(item => item.Product)
            .AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
    }

    private static QuoteItem ToEntity(Guid quoteId, int sequence, QuoteItemRequest item) => new()
    {
        QuoteId = quoteId,
        SequenceNumber = sequence,
        ProductId = item.ProductId,
        Description = item.Description!.Trim(),
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice,
        DiscountPercent = item.DiscountPercent
    };

    private static QuoteResponse ToResponse(Quote quote) => new(
        quote.Id, quote.Code, quote.CustomerId, quote.Customer.Name, quote.Customer.Code, quote.Status,
        quote.ValidUntil, quote.Notes, quote.CreatedAt, quote.SentAt, quote.AcceptedAt, quote.RejectedAt, quote.ConvertedAt,
        QuotePricing.Total(quote.Items),
        quote.Items
            .OrderBy(item => item.SequenceNumber)
            .Select(item => new QuoteItemResponse(
                item.Id, item.SequenceNumber, item.ProductId, item.Product?.Code, item.Product?.Name,
                item.Description, item.Quantity, item.UnitPrice, item.DiscountPercent, QuotePricing.LineTotal(item)))
            .ToList());

    private void AddAudit(string action, Quote quote, string details) => _dbContext.AuditLogs.Add(new AuditLog
    {
        Action = action,
        EntityType = "Quote",
        EntityId = quote.Id,
        UserName = User.FindFirstValue(ClaimTypes.Name),
        Details = details
    });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record QuoteItemRequest(Guid? ProductId, string? Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent = 0);

public sealed record SaveQuoteRequest(Guid CustomerId, DateTime? ValidUntil, string? Notes, List<QuoteItemRequest>? Items);

public sealed record ConvertQuoteRequest(DateTime? DueDate, Guid? AreaId);

public sealed record QuoteSummaryResponse(
    Guid Id, string Code, Guid CustomerId, string CustomerName, string Status,
    DateTime CreatedAt, DateTime? ValidUntil, DateTime? ConvertedAt, int ItemCount, decimal Total);

public sealed record QuoteItemResponse(
    Guid Id, int SequenceNumber, Guid? ProductId, string? ProductCode, string? ProductName,
    string Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineTotal);

public sealed record QuoteResponse(
    Guid Id, string Code, Guid CustomerId, string CustomerName, string CustomerCode, string Status,
    DateTime? ValidUntil, string? Notes, DateTime CreatedAt, DateTime? SentAt, DateTime? AcceptedAt,
    DateTime? RejectedAt, DateTime? ConvertedAt, decimal Total, List<QuoteItemResponse> Items);

public sealed record ConvertedWorkOrderResponse(Guid Id, string Code, string ProductCode, decimal Quantity);

public sealed record ConvertQuoteResponse(Guid QuoteId, string QuoteCode, List<ConvertedWorkOrderResponse> WorkOrders);

public sealed record MaterialCostLineResponse(string MaterialCode, decimal Quantity, decimal? UnitPrice, decimal? LineCost);

public sealed record ProductMaterialCostResponse(
    Guid ProductId, string ProductCode, decimal MaterialCost, int MissingPriceCount, List<MaterialCostLineResponse> Lines);
