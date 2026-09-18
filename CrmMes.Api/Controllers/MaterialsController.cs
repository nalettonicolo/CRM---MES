using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/materials")]
public class MaterialsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public MaterialsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MaterialResponse>>> GetMaterials(
        [FromQuery] string? q = null,
        [FromQuery] bool activeOnly = true,
        [FromQuery] bool belowMinimumOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Materials.AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(material => material.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            query = query.Where(material =>
                material.Code.Contains(search) || material.Name.Contains(search));
        }

        if (belowMinimumOnly)
        {
            query = query.Where(material => material.MinStock > 0 && material.Stock <= material.MinStock);
        }

        var materials = await query
            .OrderBy(material => material.Code)
            .Take(100)
            .Select(material => new MaterialResponse(
                material.Id,
                material.Code,
                material.Name,
                material.Unit,
                material.Stock,
                material.MinStock,
                material.IsActive,
                material.Stock <= material.MinStock))
            .ToListAsync(cancellationToken);

        return Ok(materials);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MaterialResponse>> GetMaterial(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var material = await _dbContext.Materials
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new MaterialResponse(
                item.Id,
                item.Code,
                item.Name,
                item.Unit,
                item.Stock,
                item.MinStock,
                item.IsActive,
                item.Stock <= item.MinStock))
            .SingleOrDefaultAsync(cancellationToken);

        return material is null ? NotFound() : Ok(material);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<MaterialResponse>> CreateMaterial(
        CreateMaterialRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Code e nome materiale sono obbligatori." });
        }

        var codeExists = await _dbContext.Materials
            .AnyAsync(material => material.Code == code, cancellationToken);

        if (codeExists)
        {
            return Conflict(new { message = $"Il codice materiale '{code}' esiste già." });
        }

        var material = new Material
        {
            Code = code,
            Name = name,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "pz" : request.Unit.Trim(),
            Stock = request.Stock,
            MinStock = request.MinStock
        };

        _dbContext.Materials.Add(material);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new MaterialResponse(
            material.Id,
            material.Code,
            material.Name,
            material.Unit,
            material.Stock,
            material.MinStock,
            material.IsActive,
            material.Stock <= material.MinStock);

        return CreatedAtAction(nameof(GetMaterial), new { id = material.Id }, response);
    }

    [Authorize(Policy = "Warehouse")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateMaterial(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var material = await _dbContext.Materials
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (material is null)
        {
            return NotFound();
        }

        material.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

public sealed record CreateMaterialRequest(
    string Code,
    string Name,
    string? Unit,
    decimal Stock,
    decimal MinStock);

public sealed record MaterialResponse(
    Guid Id,
    string Code,
    string Name,
    string Unit,
    decimal Stock,
    decimal MinStock,
    bool IsActive,
    bool BelowMinimum);
