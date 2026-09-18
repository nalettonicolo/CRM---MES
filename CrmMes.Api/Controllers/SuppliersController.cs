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
            .Select(supplier => new SupplierResponse(supplier.Id, supplier.Name, supplier.Code, supplier.Email, supplier.Phone, supplier.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(suppliers);
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
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim()
        };

        _dbContext.Suppliers.Add(supplier);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new SupplierResponse(supplier.Id, supplier.Name, supplier.Code, supplier.Email, supplier.Phone, supplier.IsActive);
        return Created($"api/suppliers/{supplier.Id}", response);
    }
}

public sealed record SupplierResponse(Guid Id, string Name, string Code, string? Email, string? Phone, bool IsActive);

public sealed record CreateSupplierRequest(string? Name, string? Code, string? Email, string? Phone);
