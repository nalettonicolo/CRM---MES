using System.Globalization;
using System.Security.Claims;
using ClosedXML.Excel;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Product master data: what to build, its bill of materials and its work cycle (routing).
/// Deliberately sector-agnostic — a "product" can be an electrical panel, a piece of furniture, a
/// mechanical assembly, anything with a recipe of materials and production phases.</summary>
[ApiController]
[Authorize]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public ProductsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductSummaryResponse>>> GetProducts(
        [FromQuery] bool activeOnly = true,
        [FromQuery] string? q = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Products.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(product => product.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            query = query.Where(product => product.Code.Contains(search) || product.Name.Contains(search));
        }

        var products = await query
            .OrderBy(product => product.Code)
            .Take(100)
            .Select(product => new ProductSummaryResponse(
                product.Id,
                product.Code,
                product.Name,
                product.IsActive,
                product.BillOfMaterial.Count,
                product.RoutingSteps.Count))
            .ToListAsync(cancellationToken);

        return Ok(products);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> GetProduct(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.BillOfMaterial)
            .Include(p => p.RoutingSteps)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        return product is null ? NotFound() : Ok(ToResponse(product));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPost]
    public async Task<ActionResult<ProductResponse>> CreateProduct(
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code?.Trim();
        var name = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Codice e nome prodotto sono obbligatori." });
        }

        if (await _dbContext.Products.AnyAsync(p => p.Code == code, cancellationToken))
        {
            return Conflict(new { message = $"Il codice prodotto '{code}' esiste già." });
        }

        var product = new Product
        {
            Code = code,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
        };

        _dbContext.Products.Add(product);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Action = "ProductCreated",
            EntityType = "Product",
            EntityId = product.Id,
            UserName = GetCurrentUserName(),
            Details = $"Prodotto {product.Code} creato."
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, ToResponse(product));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> EditProduct(
        Guid id,
        EditProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Il nome prodotto è obbligatorio." });
        }

        var product = await _dbContext.Products
            .Include(p => p.BillOfMaterial)
            .Include(p => p.RoutingSteps)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        product.Name = name;
        product.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(product));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateProduct(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        product.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPut("{id:guid}/bom")]
    public async Task<ActionResult<ProductResponse>> ReplaceBillOfMaterial(
        Guid id,
        ReplaceBillOfMaterialRequest request,
        CancellationToken cancellationToken = default) =>
        await ReplaceBillOfMaterialCoreAsync(id, request.Items, cancellationToken);

    /// <summary>Imports the bill of material for a product from an .xlsx or .csv file (columns:
    /// materialCode, quantity, notes) and replaces its current BOM, reusing the same validation
    /// and replace logic as the manual JSON endpoint above.</summary>
    [Authorize(Policy = "Warehouse")]
    [HttpPost("{id:guid}/bom/import")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<ProductResponse>> ImportBillOfMaterial(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Selezionare un file Excel (.xlsx) o CSV non vuoto." });
        }

        List<BillOfMaterialItemRequest> items;
        try
        {
            items = ParseBillOfMaterialFile(file);
        }
        catch (Exception exception)
        {
            return BadRequest(new { message = $"Impossibile leggere il file: {exception.Message}" });
        }

        if (items.Count == 0)
        {
            return BadRequest(new { message = "Il file non contiene righe valide. Colonne richieste: materialCode, quantity (opzionale: notes)." });
        }

        return await ReplaceBillOfMaterialCoreAsync(id, items, cancellationToken);
    }

    private async Task<ActionResult<ProductResponse>> ReplaceBillOfMaterialCoreAsync(
        Guid id,
        List<BillOfMaterialItemRequest>? items,
        CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
        {
            return BadRequest(new { message = "La distinta base deve contenere almeno una riga." });
        }

        if (items.Any(item => string.IsNullOrWhiteSpace(item.MaterialCode) || item.Quantity <= 0))
        {
            return BadRequest(new { message = "Ogni riga deve avere codice materiale e quantità maggiore di zero." });
        }

        var product = await _dbContext.Products
            .Include(p => p.BillOfMaterial)
            .Include(p => p.RoutingSteps)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var codes = items.Select(item => item.MaterialCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var knownCodes = await _dbContext.Materials
            .Where(material => material.IsActive && codes.Contains(material.Code))
            .Select(material => material.Code)
            .ToListAsync(cancellationToken);
        var knownCodesSet = knownCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownCodes = codes.Where(code => !knownCodesSet.Contains(code)).ToArray();
        if (unknownCodes.Length > 0)
        {
            return BadRequest(new { message = "Codici materiale non trovati o non attivi nel catalogo.", materials = unknownCodes });
        }

        foreach (var item in product.BillOfMaterial.ToList())
        {
            product.BillOfMaterial.Remove(item);
        }

        foreach (var requestItem in items)
        {
            var item = new BillOfMaterialItem
            {
                ProductId = product.Id,
                MaterialCode = requestItem.MaterialCode.Trim(),
                Quantity = requestItem.Quantity,
                Notes = string.IsNullOrWhiteSpace(requestItem.Notes) ? null : requestItem.Notes.Trim()
            };
            // Adding to the tracked DbSet is enough: EF fixes up product.BillOfMaterial automatically
            // since the parent is already tracked with the collection loaded. Also calling
            // product.BillOfMaterial.Add(item) here would duplicate the entry in that in-memory list.
            _dbContext.BillOfMaterialItems.Add(item);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(product));
    }

    private static List<BillOfMaterialItemRequest> ParseBillOfMaterialFile(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        using var stream = file.OpenReadStream();
        return extension switch
        {
            ".csv" => ParseBillOfMaterialCsv(stream),
            _ => ParseBillOfMaterialExcel(stream)
        };
    }

    private static List<BillOfMaterialItemRequest> ParseBillOfMaterialExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        var lastRow = worksheet?.LastRowUsed();
        if (worksheet is null || lastRow is null)
        {
            return [];
        }

        var headers = ParseBomHeaders(worksheet.Row(1).CellsUsed().Select(cell => cell.GetString()).ToList());
        var items = new List<BillOfMaterialItemRequest>();
        for (var rowNumber = 2; rowNumber <= lastRow.RowNumber(); rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty() || !headers.TryGetValue("materialcode", out var codeColumn))
            {
                continue;
            }

            var materialCode = row.Cell(codeColumn + 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(materialCode))
            {
                continue;
            }

            var quantity = headers.TryGetValue("quantity", out var qtyColumn)
                ? ParseDecimal(row.Cell(qtyColumn + 1).GetString())
                : 0;
            var notes = headers.TryGetValue("notes", out var notesColumn) ? row.Cell(notesColumn + 1).GetString() : null;

            items.Add(new BillOfMaterialItemRequest(materialCode, quantity, notes));
        }

        return items;
    }

    private static List<BillOfMaterialItemRequest> ParseBillOfMaterialCsv(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return [];
        }

        var headers = ParseBomHeaders(headerLine.Split(',').Select(value => value.Trim()).ToList());
        var items = new List<BillOfMaterialItemRequest>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || !headers.TryGetValue("materialcode", out var codeColumn))
            {
                continue;
            }

            var values = line.Split(',');
            var materialCode = codeColumn < values.Length ? values[codeColumn].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(materialCode))
            {
                continue;
            }

            var quantity = headers.TryGetValue("quantity", out var qtyColumn) && qtyColumn < values.Length
                ? ParseDecimal(values[qtyColumn])
                : 0;
            var notes = headers.TryGetValue("notes", out var notesColumn) && notesColumn < values.Length
                ? values[notesColumn].Trim()
                : null;

            items.Add(new BillOfMaterialItemRequest(materialCode, quantity, notes));
        }

        return items;
    }

    private static Dictionary<string, int> ParseBomHeaders(IReadOnlyList<string> headerValues) =>
        headerValues
            .Select((value, index) => new { Key = value.Trim().ToLowerInvariant(), index })
            .Where(item => !string.IsNullOrEmpty(item.Key))
            .ToDictionary(item => item.Key, item => item.index);

    private static decimal ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;

    [Authorize(Policy = "Warehouse")]
    [HttpPut("{id:guid}/routing")]
    public async Task<ActionResult<ProductResponse>> ReplaceRouting(
        Guid id,
        ReplaceRoutingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Steps is null || request.Steps.Count == 0)
        {
            return BadRequest(new { message = "Il ciclo di lavoro deve contenere almeno una fase." });
        }

        if (request.Steps.Any(step => string.IsNullOrWhiteSpace(step.Name) || step.EstimatedMinutes < 0))
        {
            return BadRequest(new { message = "Ogni fase deve avere un nome e una durata stimata non negativa." });
        }

        var product = await _dbContext.Products
            .Include(p => p.BillOfMaterial)
            .Include(p => p.RoutingSteps)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        foreach (var step in product.RoutingSteps.ToList())
        {
            product.RoutingSteps.Remove(step);
        }

        var sequence = 1;
        foreach (var requestStep in request.Steps)
        {
            var step = new RoutingStep
            {
                ProductId = product.Id,
                SequenceNumber = sequence++,
                Name = requestStep.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(requestStep.Description) ? null : requestStep.Description.Trim(),
                WorkCenter = string.IsNullOrWhiteSpace(requestStep.WorkCenter) ? null : requestStep.WorkCenter.Trim(),
                EstimatedMinutes = requestStep.EstimatedMinutes
            };
            // See the note in ReplaceBillOfMaterial: adding to the DbSet alone lets EF fix up
            // product.RoutingSteps automatically, avoiding a duplicate entry in that list.
            _dbContext.RoutingSteps.Add(step);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(product));
    }

    private string? GetCurrentUserName() => User.FindFirstValue(ClaimTypes.Name);

    private static ProductResponse ToResponse(Product product)
    {
        return new ProductResponse(
            product.Id,
            product.Code,
            product.Name,
            product.Description,
            product.IsActive,
            product.BillOfMaterial
                .Select(item => new BillOfMaterialItemResponse(item.Id, item.MaterialCode, item.Quantity, item.Notes))
                .ToList(),
            product.RoutingSteps
                .OrderBy(step => step.SequenceNumber)
                .Select(step => new RoutingStepResponse(step.Id, step.SequenceNumber, step.Name, step.Description, step.WorkCenter, step.EstimatedMinutes))
                .ToList());
    }
}

public sealed record CreateProductRequest(string? Code, string? Name, string? Description);
public sealed record EditProductRequest(string? Name, string? Description);

public sealed record BillOfMaterialItemRequest(string MaterialCode, decimal Quantity, string? Notes);
public sealed record ReplaceBillOfMaterialRequest(List<BillOfMaterialItemRequest> Items);

public sealed record RoutingStepRequest(string Name, string? Description, string? WorkCenter, decimal EstimatedMinutes);
public sealed record ReplaceRoutingRequest(List<RoutingStepRequest> Steps);

public sealed record ProductSummaryResponse(Guid Id, string Code, string Name, bool IsActive, int BomItemCount, int RoutingStepCount);

public sealed record ProductResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<BillOfMaterialItemResponse> BillOfMaterial,
    IReadOnlyList<RoutingStepResponse> RoutingSteps);

public sealed record BillOfMaterialItemResponse(Guid Id, string MaterialCode, decimal Quantity, string? Notes);

public sealed record RoutingStepResponse(Guid Id, int SequenceNumber, string Name, string? Description, string? WorkCenter, decimal EstimatedMinutes);
