using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Piano di controllo qualità per prodotto: quali caratteristiche misurare e con quale
/// tolleranza. Vedi QualityMeasurementsController per le misurazioni effettive registrate contro questi
/// checkpoint e il certificato di conformità generato da esse.</summary>
[ApiController]
[Authorize]
[Route("api/quality-checkpoints")]
public class QualityCheckpointsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public QualityCheckpointsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<QualityCheckpointResponse>>> GetCheckpoints(
        [FromQuery] Guid? productId = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.QualityCheckpoints.AsNoTracking().AsQueryable();
        if (productId.HasValue)
        {
            query = query.Where(c => c.ProductId == productId);
        }

        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var checkpoints = await query
            .OrderBy(c => c.Name)
            .Select(c => new QualityCheckpointResponse(
                c.Id, c.ProductId, c.Name, c.Unit, c.NominalValue, c.LowerLimit, c.UpperLimit, c.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(checkpoints);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<QualityCheckpointResponse>> CreateCheckpoint(
        CreateQualityCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Il nome della caratteristica è obbligatorio." });
        }

        if (request.LowerLimit.HasValue && request.UpperLimit.HasValue && request.LowerLimit > request.UpperLimit)
        {
            return BadRequest(new { message = "Il limite inferiore non può essere maggiore del limite superiore." });
        }

        var product = await _dbContext.Products.SingleOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);
        if (product is null)
        {
            return BadRequest(new { message = "Prodotto non trovato." });
        }

        var checkpoint = new QualityCheckpoint
        {
            ProductId = product.Id,
            Name = name,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim(),
            NominalValue = request.NominalValue,
            LowerLimit = request.LowerLimit,
            UpperLimit = request.UpperLimit
        };

        _dbContext.QualityCheckpoints.Add(checkpoint);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new QualityCheckpointResponse(
            checkpoint.Id, checkpoint.ProductId, checkpoint.Name, checkpoint.Unit,
            checkpoint.NominalValue, checkpoint.LowerLimit, checkpoint.UpperLimit, checkpoint.IsActive);
        return Created($"api/quality-checkpoints/{checkpoint.Id}", response);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateCheckpoint(Guid id, CancellationToken cancellationToken = default)
    {
        var checkpoint = await _dbContext.QualityCheckpoints.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (checkpoint is null)
        {
            return NotFound();
        }

        checkpoint.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record CreateQualityCheckpointRequest(
    Guid ProductId, string? Name, string? Unit, decimal? NominalValue, decimal? LowerLimit, decimal? UpperLimit);

public sealed record QualityCheckpointResponse(
    Guid Id, Guid ProductId, string Name, string? Unit, decimal? NominalValue, decimal? LowerLimit, decimal? UpperLimit, bool IsActive);
