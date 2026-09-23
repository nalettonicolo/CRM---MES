using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/suppliers")]
public class SuppliersController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public SuppliersController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierResponse>>> GetSuppliers(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Suppliers.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(supplier => supplier.IsActive);
        }

        var suppliers = await query
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => new SupplierResponse(supplier.Id, supplier.Name, supplier.Code, supplier.Email, supplier.Phone, supplier.Website, supplier.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(suppliers);
    }

    /// <summary>Dettaglio fornitore in una sola chiamata: anagrafica + ordini d'acquisto + voci di
    /// catalogo (materiali collegati con part number/prezzo/lead time, da import o inseriti a mano).</summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<SupplierDetailResponse>> GetSupplierDetail(Guid id, CancellationToken cancellationToken = default)
    {
        var supplier = await _dbContext.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (supplier is null)
        {
            return NotFound();
        }

        var purchaseOrders = await _dbContext.PurchaseOrders.AsNoTracking()
            .Where(order => order.SupplierId == id)
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new SupplierPurchaseOrderResponse(order.Id, order.Code, order.Status, order.CreatedAt))
            .ToListAsync(cancellationToken);

        var catalogEntries = await _dbContext.MaterialSuppliers.AsNoTracking()
            .Include(link => link.Material)
            .Where(link => link.SupplierId == id)
            .OrderBy(link => link.Material.Code)
            .Select(link => new SupplierCatalogEntryResponse(
                link.Material.Code, link.Material.Name, link.PartNumber, link.Description, link.UnitPrice, link.LeadTimeDays))
            .ToListAsync(cancellationToken);

        return Ok(new SupplierDetailResponse(
            supplier.Id, supplier.Name, supplier.Code, supplier.Email, supplier.Phone, supplier.Website, supplier.IsActive,
            purchaseOrders, catalogEntries));
    }

    [Authorize(Policy = "Purchasing")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierResponse>> EditSupplier(
        Guid id,
        EditSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        var supplier = await _dbContext.Suppliers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (supplier is null)
        {
            return NotFound();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Il nome è obbligatorio." });
        }

        supplier.Name = name;
        supplier.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        supplier.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        supplier.Website = string.IsNullOrWhiteSpace(request.Website) ? null : request.Website.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new SupplierResponse(supplier.Id, supplier.Name, supplier.Code, supplier.Email, supplier.Phone, supplier.Website, supplier.IsActive));
    }

    [Authorize(Policy = "Purchasing")]
    [HttpPost]
    public async Task<ActionResult<SupplierResponse>> CreateSupplier(
        CreateSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var code = request.Code?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { message = "Nome e codice fornitore sono obbligatori." });
        }

        if (await _dbContext.Suppliers.AnyAsync(supplier => supplier.Code == code, cancellationToken))
        {
            return Conflict(new { message = $"Il codice fornitore '{code}' esiste già." });
        }

        var supplier = new Supplier
        {
            Name = name,
            Code = code,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Website = string.IsNullOrWhiteSpace(request.Website) ? null : request.Website.Trim()
        };

        _dbContext.Suppliers.Add(supplier);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new SupplierResponse(supplier.Id, supplier.Name, supplier.Code, supplier.Email, supplier.Phone, supplier.Website, supplier.IsActive);
        return Created($"api/suppliers/{supplier.Id}", response);
    }
}

public sealed record SupplierResponse(Guid Id, string Name, string Code, string? Email, string? Phone, string? Website, bool IsActive);

public sealed record CreateSupplierRequest(string? Name, string? Code, string? Email, string? Phone, string? Website = null);

public sealed record EditSupplierRequest(string? Name, string? Email, string? Phone, string? Website);

public sealed record SupplierPurchaseOrderResponse(Guid Id, string Code, string Status, DateTime CreatedAt);

public sealed record SupplierCatalogEntryResponse(string MaterialCode, string MaterialName, string PartNumber, string? Description, decimal UnitPrice, decimal LeadTimeDays);

public sealed record SupplierDetailResponse(
    Guid Id, string Name, string Code, string? Email, string? Phone, string? Website, bool IsActive,
    List<SupplierPurchaseOrderResponse> PurchaseOrders, List<SupplierCatalogEntryResponse> CatalogEntries);
