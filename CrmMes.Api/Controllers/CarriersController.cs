using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/carriers")]
public class CarriersController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public CarriersController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CarrierResponse>>> GetCarriers(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Carriers.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(carrier => carrier.IsActive);
        }

        var carriers = await query
            .OrderBy(carrier => carrier.Name)
            .Select(carrier => new CarrierResponse(carrier.Id, carrier.Name, carrier.Code, carrier.Email, carrier.Phone, carrier.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(carriers);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<CarrierResponse>> CreateCarrier(
        CreateCarrierRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var code = request.Code?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { message = "Nome e codice corriere sono obbligatori." });
        }

        if (await _dbContext.Carriers.AnyAsync(carrier => carrier.Code == code, cancellationToken))
        {
            return Conflict(new { message = $"Il codice corriere '{code}' esiste già." });
        }

        var carrier = new Carrier
        {
            Name = name,
            Code = code,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim()
        };

        _dbContext.Carriers.Add(carrier);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new CarrierResponse(carrier.Id, carrier.Name, carrier.Code, carrier.Email, carrier.Phone, carrier.IsActive);
        return Created($"api/carriers/{carrier.Id}", response);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateCarrier(Guid id, CancellationToken cancellationToken = default)
    {
        var carrier = await _dbContext.Carriers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (carrier is null)
        {
            return NotFound();
        }

        carrier.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record CarrierResponse(Guid Id, string Name, string Code, string? Email, string? Phone, bool IsActive);

public sealed record CreateCarrierRequest(string? Name, string? Code, string? Email, string? Phone);
