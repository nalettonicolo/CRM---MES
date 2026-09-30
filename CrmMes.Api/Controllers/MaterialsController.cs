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
                material.Stock <= material.MinStock,
                material.ListPrice,
                material.VatRate))
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
                item.Stock <= item.MinStock,
                item.ListPrice,
                item.VatRate))
            .SingleOrDefaultAsync(cancellationToken);

        return material is null ? NotFound() : Ok(material);
    }

    /// <summary>Imports an article list (Excel .xlsx or CSV). preview=true only checks and counts; the
    /// real import runs with preview=false. update=false leaves articles already present untouched;
    /// update=true refreshes their description, unit, price, VAT and minimum stock (never the stock: that
    /// comes from lots). Nothing is deleted.</summary>
    [Authorize(Policy = "PurchasingOrWarehouse")]
    [HttpPost("import")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<ArticleImportResponse>> Import(
        IFormFile? file, [FromQuery] bool preview = true, [FromQuery] bool update = false, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Scegli un file Excel (.xlsx) o CSV." });
        }

        List<string?[]> rows;
        try
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension == ".csv" || extension == ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                rows = ArticleImport.ReadCsv(await reader.ReadToEndAsync(cancellationToken));
            }
            else if (extension == ".xlsx" || extension == ".xlsm")
            {
                using var stream = file.OpenReadStream();
                rows = ArticleImport.ReadExcel(stream);
            }
            else
            {
                return BadRequest(new { message = "Formato non supportato: salva il file come Excel (.xlsx) o CSV." });
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BadRequest(new { message = $"Il file non si legge: {exception.Message}" });
        }

        var parsed = ArticleImport.Parse(rows);
        var existing = await _dbContext.Materials.ToDictionaryAsync(m => m.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        int created = 0, updated = 0, unchanged = 0, skipped = 0;
        var samples = new List<ArticleImportSample>();

        foreach (var article in parsed.Articles)
        {
            if (existing.TryGetValue(article.Code, out var material))
            {
                if (!update)
                {
                    skipped++;
                    continue;
                }

                var changed = material.Name != article.Name || material.Unit != article.Unit
                    || (article.Price is not null && material.ListPrice != article.Price)
                    || (article.VatRate is not null && material.VatRate != article.VatRate)
                    || (article.MinStock is not null && material.MinStock != article.MinStock);
                if (!changed)
                {
                    unchanged++;
                    continue;
                }

                updated++;
                if (samples.Count < 10) samples.Add(new ArticleImportSample(article.Row, article.Code, article.Name, article.Unit, article.Price, article.VatRate, "Aggiornato"));
                if (!preview)
                {
                    material.Name = article.Name;
                    material.Unit = article.Unit;
                    material.ListPrice = article.Price ?? material.ListPrice;
                    material.VatRate = article.VatRate ?? material.VatRate;
                    material.MinStock = article.MinStock ?? material.MinStock;
                }

                continue;
            }

            created++;
            if (samples.Count < 10) samples.Add(new ArticleImportSample(article.Row, article.Code, article.Name, article.Unit, article.Price, article.VatRate, "Nuovo"));
            if (!preview)
            {
                var newMaterial = new Material
                {
                    Code = article.Code,
                    Name = article.Name,
                    Unit = article.Unit,
                    ListPrice = article.Price,
                    VatRate = article.VatRate,
                    MinStock = article.MinStock ?? 0,
                    CreatedAt = article.CreatedAt ?? DateTime.UtcNow,
                };
                _dbContext.Materials.Add(newMaterial);
                existing[article.Code] = newMaterial;
            }
        }

        if (!preview && (created > 0 || updated > 0))
        {
            _dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "ArticlesImported",
                EntityType = "Material",
                UserName = User.FindFirstValue(ClaimTypes.Name),
                Details = $"Import articoli da {file.FileName}: {created} nuovi, {updated} aggiornati, {skipped} già presenti, {parsed.Errors.Count} righe con errori."
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new ArticleImportResponse(
            preview, parsed.Articles.Count + parsed.Errors.Count(e => e.Row > 0), created, updated, unchanged, skipped,
            parsed.Errors.Select(e => new ArticleImportIssue(e.Row, e.Code, e.Message)).Take(500).ToList(),
            parsed.Warnings.Select(w => new ArticleImportIssue(w.Row, w.Code, w.Message)).Take(500).ToList(),
            parsed.Errors.Count, parsed.Warnings.Count, parsed.Columns, samples));
    }

    /// <summary>All articles in the same columns the import reads (Articolo, Descrizione, CodIVA, UMBase,
    /// PrezzoBase, DataCreazione...), so the file can be edited and imported back.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Materials.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(m => m.IsActive);
        }

        var materials = await query.OrderBy(m => m.Code).ToListAsync(cancellationToken);
        return File(ArticleImport.Export(materials), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"articoli-{DateTime.Now:yyyyMMdd}.xlsx");
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

        // Opening stock becomes its own traceable lot, so the material's lot ledger starts consistent
        // with Material.Stock from day one instead of only covering intake from this point forward.
        if (material.Stock > 0)
        {
            _dbContext.MaterialLots.Add(new MaterialLot
            {
                MaterialCode = material.Code,
                LotNumber = $"INIZIALE-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                Quantity = material.Stock,
                InitialQuantity = material.Stock,
                Notes = "Giacenza iniziale dichiarata alla creazione del materiale."
            });
        }

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
    bool BelowMinimum,
    decimal? ListPrice = null,
    decimal? VatRate = null);

public sealed record ArticleImportIssue(int Row, string? Code, string Message);

public sealed record ArticleImportSample(int Row, string Code, string Name, string Unit, decimal? Price, decimal? VatRate, string Outcome);

public sealed record ArticleImportResponse(
    bool Preview, int Rows, int Created, int Updated, int Unchanged, int Skipped,
    List<ArticleImportIssue> Errors, List<ArticleImportIssue> Warnings, int ErrorCount, int WarningCount,
    Dictionary<string, string> Columns, List<ArticleImportSample> Samples);
