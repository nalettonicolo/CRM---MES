using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Food sector: ingredient names and allergens on materials, label data on products, the label
/// of a produced lot (ingredients in descending order of weight, allergens emphasised, use-by date), and
/// SSCC pallet codes.</summary>
[ApiController]
[Authorize]
[Route("api/food")]
public class FoodController : ControllerBase
{
    private const int MaxSsccAttempts = 3;
    private readonly ApplicationDbContext _dbContext;

    public FoodController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("allergens")]
    public ActionResult<IEnumerable<AllergenResponse>> GetAllergens() =>
        Ok(FoodAllergens.Names.Select(pair => new AllergenResponse(pair.Key, pair.Value)));

    [HttpGet("materials/{id:guid}")]
    public async Task<ActionResult<MaterialFoodInfoResponse>> GetMaterial(Guid id, CancellationToken cancellationToken = default)
    {
        var material = await _dbContext.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return material is null ? NotFound() : Ok(ToResponse(material));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPut("materials/{id:guid}")]
    public async Task<ActionResult<MaterialFoodInfoResponse>> SaveMaterial(
        Guid id, SaveMaterialFoodInfoRequest request, CancellationToken cancellationToken = default)
    {
        var material = await _dbContext.Materials.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (material is null)
        {
            return NotFound();
        }

        var unknown = (request.Allergens ?? []).Where(key => !FoodAllergens.IsKnown(key.Trim().ToLowerInvariant())).ToList();
        if (unknown.Count > 0)
        {
            return BadRequest(new { message = $"Allergeni non riconosciuti: {string.Join(", ", unknown)}." });
        }

        material.IngredientName = Clean(request.IngredientName);
        var allergens = FoodAllergens.Normalize(request.Allergens ?? []);
        material.Allergens = allergens.Count == 0 ? null : string.Join(',', allergens);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(material));
    }

    [HttpGet("products/{id:guid}")]
    public async Task<ActionResult<ProductFoodInfoResponse>> GetProduct(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return product is null ? NotFound() : Ok(ToResponse(product));
    }

    [Authorize(Policy = "Warehouse")]
    [HttpPut("products/{id:guid}")]
    public async Task<ActionResult<ProductFoodInfoResponse>> SaveProduct(
        Guid id, SaveProductFoodInfoRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (request.ShelfLifeDays is < 1 or > 3650)
        {
            return BadRequest(new { message = "Durata tra 1 e 3650 giorni." });
        }

        product.SalesName = Clean(request.SalesName);
        product.ShelfLifeDays = request.ShelfLifeDays;
        product.UseByDate = request.UseByDate;
        product.StorageConditions = Clean(request.StorageConditions);
        product.NetQuantity = Clean(request.NetQuantity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(product));
    }

    /// <summary>Label of the lot made by a work order. Ingredients come from the bill of materials in
    /// descending order of quantity per unit (Reg. 1169/2011, art. 18); an ingredient is printed with the
    /// allergens it contains, and the product's allergens are the union of its ingredients'. Materials of
    /// the recipe that are missing from the catalog are reported, never silently dropped.</summary>
    [HttpGet("work-orders/{workOrderId:guid}/label")]
    public async Task<ActionResult<FoodLabelResponse>> GetLabel(Guid workOrderId, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.WorkOrders.AsNoTracking()
            .Include(o => o.Product).ThenInclude(p => p.BillOfMaterial)
            .FirstOrDefaultAsync(o => o.Id == workOrderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var codes = order.Product.BillOfMaterial.Select(line => line.MaterialCode).ToList();
        var materials = await _dbContext.Materials.AsNoTracking()
            .Where(m => codes.Contains(m.Code))
            .ToDictionaryAsync(m => m.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var warnings = new List<string>();
        var ingredients = new List<FoodLabelIngredient>();
        foreach (var group in order.Product.BillOfMaterial
                     .GroupBy(line => line.MaterialCode, StringComparer.OrdinalIgnoreCase)
                     .Select(group => (Code: group.Key, Quantity: group.Sum(line => line.Quantity)))
                     .OrderByDescending(entry => entry.Quantity)
                     .ThenBy(entry => entry.Code, StringComparer.OrdinalIgnoreCase))
        {
            if (!materials.TryGetValue(group.Code, out var material))
            {
                warnings.Add($"Il materiale {group.Code} della distinta non è in anagrafica: manca dall'etichetta.");
                continue;
            }

            if (material.IngredientName is null && material.Allergens is null)
            {
                warnings.Add($"{material.Code} senza dati alimentari: verifica nome ingrediente e allergeni.");
            }

            var allergens = FoodAllergens.Parse(material.Allergens);
            ingredients.Add(new FoodLabelIngredient(
                material.Code, material.IngredientName ?? material.Name, group.Quantity, allergens,
                allergens.Select(key => FoodAllergens.Names[key]).ToList()));
        }

        if (order.Product.ShelfLifeDays is null)
        {
            warnings.Add("Durata del prodotto non impostata: manca la data di scadenza.");
        }

        var productionDate = (order.CompletedAt ?? order.ReleasedAt ?? DateTime.UtcNow).Date;
        var expiry = order.Product.ShelfLifeDays is { } days ? productionDate.AddDays(days) : (DateTime?)null;
        var allAllergens = FoodAllergens.Normalize(ingredients.SelectMany(i => i.Allergens));
        var company = await _dbContext.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        return Ok(new FoodLabelResponse(
            order.Id, order.Code, order.Product.Code, order.Product.SalesName ?? order.Product.Name,
            order.ProductLotNumber, order.Quantity, productionDate, expiry, order.Product.UseByDate,
            order.Product.StorageConditions, order.Product.NetQuantity,
            company?.CompanyName, company?.Address,
            ingredients, allAllergens.Select(key => FoodAllergens.Names[key]).ToList(), warnings));
    }

    /// <summary>Allocates the next SSCC for a pallet: the serial counter lives on the company profile and
    /// the SSCC column is unique, so two pallets labelled at the same moment can never share a code.</summary>
    [Authorize(Policy = "Warehouse")]
    [HttpPost("logistic-units")]
    public async Task<ActionResult<LogisticUnitResponse>> CreateLogisticUnit(
        CreateLogisticUnitRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return BadRequest(new { message = "La quantità deve essere maggiore di zero." });
        }

        WorkOrder? order = null;
        if (request.WorkOrderId is { } workOrderId)
        {
            order = await _dbContext.WorkOrders.AsNoTracking().Include(o => o.Product)
                .FirstOrDefaultAsync(o => o.Id == workOrderId, cancellationToken);
            if (order is null)
            {
                return BadRequest(new { message = "Commessa non trovata." });
            }
        }

        if (request.TransportDocumentId is { } documentId && !await _dbContext.TransportDocuments.AnyAsync(d => d.Id == documentId, cancellationToken))
        {
            return BadRequest(new { message = "DDT non trovato." });
        }

        for (var attempt = 1; ; attempt++)
        {
            var company = await _dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
            if (company is null || !Gs1.IsValidCompanyPrefix(company.Gs1CompanyPrefix))
            {
                return Conflict(new { message = "Imposta il prefisso aziendale GS1 in Azienda e settore prima di stampare etichette SSCC." });
            }

            var serial = company.LastSsccSerial + 1;
            if (serial > Gs1.MaxSerial(company.Gs1CompanyPrefix!))
            {
                return Conflict(new { message = "Numeratore SSCC esaurito per questo prefisso GS1." });
            }

            company.LastSsccSerial = serial;
            var productionDate = (order?.CompletedAt ?? order?.ReleasedAt ?? DateTime.UtcNow).Date;
            var unit = new LogisticUnit
            {
                Sscc = Gs1.Sscc(company.Gs1CompanyPrefix!, serial),
                WorkOrderId = order?.Id,
                TransportDocumentId = request.TransportDocumentId,
                ProductCode = order?.Product.Code,
                ProductName = order is null ? null : order.Product.SalesName ?? order.Product.Name,
                LotNumber = order?.ProductLotNumber,
                Quantity = request.Quantity ?? order?.Quantity,
                BestBefore = order?.Product.ShelfLifeDays is { } days ? productionDate.AddDays(days) : null,
                CreatedBy = User.FindFirstValue(ClaimTypes.Name)
            };
            _dbContext.LogisticUnits.Add(unit);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Ok(ToResponse(unit));
            }
            catch (DbUpdateException) when (attempt < MaxSsccAttempts)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    [HttpGet("logistic-units")]
    public async Task<ActionResult<IEnumerable<LogisticUnitResponse>>> GetLogisticUnits(
        [FromQuery] Guid? workOrderId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.LogisticUnits.AsNoTracking();
        if (workOrderId.HasValue)
        {
            query = query.Where(u => u.WorkOrderId == workOrderId);
        }

        var units = await query.OrderByDescending(u => u.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return Ok(units.Select(ToResponse));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("gs1-prefix")]
    public async Task<IActionResult> SetGs1Prefix(SetGs1PrefixRequest request, CancellationToken cancellationToken = default)
    {
        var prefix = request.CompanyPrefix?.Trim();
        if (!Gs1.IsValidCompanyPrefix(prefix))
        {
            return BadRequest(new { message = "Il prefisso aziendale GS1 ha da 7 a 10 cifre." });
        }

        var company = await _dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return Conflict(new { message = "Configura prima i dati dell'azienda." });
        }

        if (company.Gs1CompanyPrefix != prefix)
        {
            // A new prefix starts a new numbering space: serials restart, codes stay unique worldwide.
            company.Gs1CompanyPrefix = prefix;
            company.LastSsccSerial = await _dbContext.LogisticUnits.CountAsync(u => u.Sscc.StartsWith("0" + prefix), cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static MaterialFoodInfoResponse ToResponse(Material material)
    {
        var allergens = FoodAllergens.Parse(material.Allergens);
        return new MaterialFoodInfoResponse(material.Id, material.Code, material.Name, material.IngredientName, allergens);
    }

    private static ProductFoodInfoResponse ToResponse(Product product) => new(
        product.Id, product.Code, product.Name, product.SalesName, product.ShelfLifeDays, product.UseByDate,
        product.StorageConditions, product.NetQuantity);

    private static LogisticUnitResponse ToResponse(LogisticUnit unit) => new(
        unit.Id, unit.Sscc, unit.WorkOrderId, unit.TransportDocumentId, unit.ProductCode, unit.ProductName,
        unit.LotNumber, unit.Quantity, unit.BestBefore, unit.CreatedAt);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AllergenResponse(string Key, string Name);

public sealed record MaterialFoodInfoResponse(Guid Id, string Code, string Name, string? IngredientName, List<string> Allergens);

public sealed record SaveMaterialFoodInfoRequest(string? IngredientName, List<string>? Allergens);

public sealed record ProductFoodInfoResponse(
    Guid Id, string Code, string Name, string? SalesName, int? ShelfLifeDays, bool UseByDate,
    string? StorageConditions, string? NetQuantity);

public sealed record SaveProductFoodInfoRequest(
    string? SalesName, int? ShelfLifeDays, bool UseByDate, string? StorageConditions, string? NetQuantity);

public sealed record FoodLabelIngredient(string MaterialCode, string Name, decimal QuantityPerUnit, List<string> Allergens, List<string> AllergenNames);

public sealed record FoodLabelResponse(
    Guid WorkOrderId, string WorkOrderCode, string ProductCode, string SalesName, string? LotNumber, decimal Quantity,
    DateTime ProductionDate, DateTime? ExpiryDate, bool UseByDate, string? StorageConditions, string? NetQuantity,
    string? ProducerName, string? ProducerAddress,
    List<FoodLabelIngredient> Ingredients, List<string> Allergens, List<string> Warnings);

public sealed record CreateLogisticUnitRequest(Guid? WorkOrderId, Guid? TransportDocumentId, decimal? Quantity);

public sealed record LogisticUnitResponse(
    Guid Id, string Sscc, Guid? WorkOrderId, Guid? TransportDocumentId, string? ProductCode, string? ProductName,
    string? LotNumber, decimal? Quantity, DateTime? BestBefore, DateTime CreatedAt);

public sealed record SetGs1PrefixRequest(string? CompanyPrefix);
