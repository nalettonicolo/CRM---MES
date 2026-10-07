using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/locations")]
public class LocationsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public LocationsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WarehouseLocationResponse>>> GetLocations(
        [FromQuery] Guid? siteId = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.WarehouseLocations.AsNoTracking();
        if (siteId.HasValue)
        {
            query = query.Where(l => l.SiteId == siteId);
        }

        if (activeOnly)
        {
            query = query.Where(l => l.IsActive);
        }

        var locations = await query
            .OrderBy(l => l.Name)
            .Select(l => new WarehouseLocationResponse(l.Id, l.Code, l.Name, l.SiteId, l.IsActive, l.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(locations);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<WarehouseLocationResponse>> CreateLocation(
        CreateWarehouseLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var code = request.Code?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { message = "Nome e codice ubicazione sono obbligatori." });
        }

        if (await _dbContext.WarehouseLocations.AnyAsync(l => l.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Esiste già un'ubicazione con questo codice." });
        }

        if (request.SiteId.HasValue &&
            !await _dbContext.Sites.AnyAsync(s => s.Id == request.SiteId && s.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Sede non trovata o non attiva." });
        }

        var location = new WarehouseLocation
        {
            Code = code,
            Name = name,
            SiteId = request.SiteId
        };

        _dbContext.WarehouseLocations.Add(location);
        AuditTrail.Add(_dbContext, User, "LocationCreated", "WarehouseLocation", location.Id, $"Ubicazione {location.Code} creata.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created($"api/locations/{location.Id}",
            new WarehouseLocationResponse(location.Id, location.Code, location.Name, location.SiteId, location.IsActive, location.CreatedAt));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WarehouseLocationResponse>> UpdateLocation(
        Guid id,
        UpdateWarehouseLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var location = await _dbContext.WarehouseLocations.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return NotFound(new { message = "Ubicazione non trovata." });
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            location.Name = request.Name.Trim();
        }

        if (request.SiteId.HasValue &&
            !await _dbContext.Sites.AnyAsync(s => s.Id == request.SiteId && s.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Sede non trovata o non attiva." });
        }

        if (request.SiteId.HasValue || request.ClearSite == true)
        {
            location.SiteId = request.ClearSite == true ? null : request.SiteId;
        }

        if (request.IsActive.HasValue)
        {
            location.IsActive = request.IsActive.Value;
        }

        AuditTrail.Add(_dbContext, User, "LocationUpdated", "WarehouseLocation", location.Id, $"Ubicazione {location.Code} modificata.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new WarehouseLocationResponse(location.Id, location.Code, location.Name, location.SiteId, location.IsActive, location.CreatedAt));
    }

    [HttpGet("{id:guid}/stock")]
    public async Task<ActionResult<IEnumerable<LocationStockResponse>>> GetLocationStock(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.WarehouseLocations.AnyAsync(l => l.Id == id, cancellationToken))
        {
            return NotFound(new { message = "Ubicazione non trovata." });
        }

        var stock = await _dbContext.LocationStocks.AsNoTracking()
            .Where(s => s.LocationId == id && s.Quantity != 0)
            .Include(s => s.Material)
            .OrderBy(s => s.Material!.Code)
            .Select(s => new LocationStockResponse(
                s.Id,
                s.MaterialId,
                s.Material!.Code,
                s.Material.Name,
                s.Material.Unit,
                s.Quantity))
            .ToListAsync(cancellationToken);

        return Ok(stock);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/stock/adjust")]
    public async Task<ActionResult<LocationStockResponse>> AdjustLocationStock(
        Guid id,
        AdjustLocationStockRequest request,
        CancellationToken cancellationToken = default)
    {
        var location = await _dbContext.WarehouseLocations.SingleOrDefaultAsync(l => l.Id == id && l.IsActive, cancellationToken);
        if (location is null)
        {
            return NotFound(new { message = "Ubicazione non trovata o non attiva." });
        }

        var material = await _dbContext.Materials.SingleOrDefaultAsync(m => m.Id == request.MaterialId && m.IsActive, cancellationToken);
        if (material is null)
        {
            return BadRequest(new { message = "Materiale non trovato o non attivo." });
        }

        if (request.Quantity < 0)
        {
            return BadRequest(new { message = "La quantità non può essere negativa." });
        }

        var stock = await _dbContext.LocationStocks
            .SingleOrDefaultAsync(s => s.LocationId == id && s.MaterialId == request.MaterialId, cancellationToken);

        var previousQuantity = stock?.Quantity ?? 0m;
        var delta = request.Quantity - previousQuantity;

        if (stock is null)
        {
            stock = new LocationStock
            {
                LocationId = id,
                MaterialId = request.MaterialId,
                Quantity = request.Quantity
            };
            _dbContext.LocationStocks.Add(stock);
        }
        else
        {
            stock.Quantity = request.Quantity;
        }

        material.Stock += delta;

        if (material.Stock < 0)
        {
            return BadRequest(new { message = "La rettifica porterebbe la giacenza totale del materiale sotto zero." });
        }

        AuditTrail.Add(_dbContext, User, "LocationStockAdjusted", "WarehouseLocation", location.Id, $"Giacenza di {material.Code} in {location.Code} rettificata a {request.Quantity} ({(delta >= 0 ? "+" : "")}{delta}).");
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new LocationStockResponse(
            stock.Id,
            material.Id,
            material.Code,
            material.Name,
            material.Unit,
            stock.Quantity));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("inventory")]
    public async Task<ActionResult<InventorySessionResponse>> OpenInventorySession(
        OpenInventorySessionRequest? request,
        CancellationToken cancellationToken = default)
    {
        var code = string.IsNullOrWhiteSpace(request?.Code)
            ? $"INV-{DateTime.UtcNow:yyyyMMdd-HHmmss}"
            : request.Code.Trim().ToUpperInvariant();

        if (await _dbContext.InventorySessions.AnyAsync(s => s.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Esiste già una sessione di inventario con questo codice." });
        }

        Guid? createdBy = null;
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(sub, out var userId))
        {
            createdBy = userId;
        }

        var session = new InventorySession
        {
            Code = code,
            Status = InventorySessionStatuses.Open,
            StartedAt = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(request?.Notes) ? null : request.Notes.Trim(),
            CreatedBy = createdBy
        };

        _dbContext.InventorySessions.Add(session);
        AuditTrail.Add(_dbContext, User, "InventorySessionOpened", "InventorySession", session.Id, $"Sessione di inventario {session.Code} aperta.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created($"api/locations/inventory/{session.Id}",
            new InventorySessionResponse(session.Id, session.Code, session.Status, session.StartedAt, session.ClosedAt, session.Notes, []));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("inventory/{sessionId:guid}/lines")]
    public async Task<ActionResult<InventoryLineResponse>> UpsertInventoryLine(
        Guid sessionId,
        UpsertInventoryLineRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.InventorySessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound(new { message = "Sessione di inventario non trovata." });
        }

        if (session.Status != InventorySessionStatuses.Open)
        {
            return BadRequest(new { message = "La sessione di inventario è chiusa e non accetta modifiche." });
        }

        if (!await _dbContext.WarehouseLocations.AnyAsync(l => l.Id == request.LocationId && l.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Ubicazione non trovata o non attiva." });
        }

        if (!await _dbContext.Materials.AnyAsync(m => m.Id == request.MaterialId && m.IsActive, cancellationToken))
        {
            return BadRequest(new { message = "Materiale non trovato o non attivo." });
        }

        if (request.CountedQuantity < 0)
        {
            return BadRequest(new { message = "La quantità contata non può essere negativa." });
        }

        var line = await _dbContext.InventoryLines
            .SingleOrDefaultAsync(l => l.SessionId == sessionId && l.LocationId == request.LocationId && l.MaterialId == request.MaterialId, cancellationToken);

        if (line is null)
        {
            var systemQty = await _dbContext.LocationStocks.AsNoTracking()
                .Where(s => s.LocationId == request.LocationId && s.MaterialId == request.MaterialId)
                .Select(s => s.Quantity)
                .SingleOrDefaultAsync(cancellationToken);

            line = new InventoryLine
            {
                SessionId = sessionId,
                LocationId = request.LocationId,
                MaterialId = request.MaterialId,
                SystemQuantity = systemQty,
                CountedQuantity = request.CountedQuantity
            };
            _dbContext.InventoryLines.Add(line);
        }
        else
        {
            line.CountedQuantity = request.CountedQuantity;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToLineResponse(line));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost("inventory/{sessionId:guid}/close")]
    public async Task<ActionResult<InventorySessionResponse>> CloseInventorySession(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.InventorySessions
            .Include(s => s.Lines)
            .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
        {
            return NotFound(new { message = "Sessione di inventario non trovata." });
        }

        if (session.Status == InventorySessionStatuses.Closed)
        {
            return BadRequest(new { message = "La sessione di inventario è già chiusa." });
        }

        foreach (var line in session.Lines.Where(l => l.CountedQuantity.HasValue))
        {
            var delta = line.CountedQuantity!.Value - line.SystemQuantity;
            if (delta == 0)
            {
                continue;
            }

            var stock = await _dbContext.LocationStocks
                .SingleOrDefaultAsync(s => s.LocationId == line.LocationId && s.MaterialId == line.MaterialId, cancellationToken);

            if (stock is null)
            {
                stock = new LocationStock
                {
                    LocationId = line.LocationId,
                    MaterialId = line.MaterialId,
                    Quantity = line.CountedQuantity.Value
                };
                _dbContext.LocationStocks.Add(stock);
            }
            else
            {
                stock.Quantity = line.CountedQuantity.Value;
            }

            var material = await _dbContext.Materials.SingleAsync(m => m.Id == line.MaterialId, cancellationToken);
            material.Stock += delta;

            if (material.Stock < 0)
            {
                return BadRequest(new { message = $"La chiusura inventario porterebbe la giacenza del materiale '{material.Code}' sotto zero." });
            }
        }

        session.Status = InventorySessionStatuses.Closed;
        session.ClosedAt = DateTime.UtcNow;
        AuditTrail.Add(_dbContext, User, "InventorySessionClosed", "InventorySession", session.Id, $"Sessione di inventario {session.Code} chiusa.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        var lines = session.Lines.Select(ToLineResponse).ToList();
        return Ok(new InventorySessionResponse(session.Id, session.Code, session.Status, session.StartedAt, session.ClosedAt, session.Notes, lines));
    }

    private static InventoryLineResponse ToLineResponse(InventoryLine line) =>
        new(line.Id, line.SessionId, line.LocationId, line.MaterialId, line.SystemQuantity, line.CountedQuantity, line.Difference);
}

public sealed record WarehouseLocationResponse(Guid Id, string Code, string Name, Guid? SiteId, bool IsActive, DateTime CreatedAt);

public sealed record CreateWarehouseLocationRequest(string? Name, string? Code, Guid? SiteId = null);

public sealed record UpdateWarehouseLocationRequest(string? Name, Guid? SiteId = null, bool? IsActive = null, bool? ClearSite = null);

public sealed record LocationStockResponse(Guid Id, Guid MaterialId, string MaterialCode, string MaterialName, string Unit, decimal Quantity);

public sealed record AdjustLocationStockRequest(Guid MaterialId, decimal Quantity, string? Reason);

public sealed record OpenInventorySessionRequest(string? Code, string? Notes);

public sealed record UpsertInventoryLineRequest(Guid LocationId, Guid MaterialId, decimal CountedQuantity);

public sealed record InventoryLineResponse(
    Guid Id,
    Guid SessionId,
    Guid LocationId,
    Guid MaterialId,
    decimal SystemQuantity,
    decimal? CountedQuantity,
    decimal? Difference);

public sealed record InventorySessionResponse(
    Guid Id,
    string Code,
    string Status,
    DateTime StartedAt,
    DateTime? ClosedAt,
    string? Notes,
    IReadOnlyList<InventoryLineResponse> Lines);
