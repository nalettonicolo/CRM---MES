using System.Globalization;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/supplier-catalog")]
public class SupplierCatalogController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public SupplierCatalogController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { message = "Il parametro q è obbligatorio." });
        }

        var search = q.Trim();
        var results = await _dbContext.MaterialSuppliers
            .AsNoTracking()
            .Include(item => item.Material)
            .Include(item => item.Supplier)
            .Where(item => item.PartNumber.Contains(search) ||
                          item.Description != null && item.Description.Contains(search) ||
                          item.Material.Code.Contains(search) ||
                          item.Material.Name.Contains(search))
            .OrderBy(item => item.Material.Code)
            .Take(100)
            .Select(item => new
            {
                item.Material.Code,
                MaterialName = item.Material.Name,
                Supplier = item.Supplier.Name,
                SupplierCode = item.Supplier.Code,
                item.PartNumber,
                item.Description,
                item.UnitPrice,
                item.LeadTimeDays
            })
            .ToListAsync(cancellationToken);

        return Ok(results);
    }

    [HttpPost("import-csv")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> ImportCsv(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Selezionare un file CSV non vuoto." });
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        using var reader = new StreamReader(file.OpenReadStream());
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return BadRequest(new { message = "Il CSV non contiene intestazioni." });
        }

        var headers = ParseCsvLine(headerLine)
            .Select((value, index) => new { Key = value.Trim().ToLowerInvariant(), index })
            .ToDictionary(item => item.Key, item => item.index);
        var required = new[] { "suppliercode", "suppliername", "code", "name", "partnumber" };
        if (required.Any(item => !headers.ContainsKey(item)))
        {
            return BadRequest(new { message = "Colonne obbligatorie: supplierCode, supplierName, code, name, partNumber." });
        }

        var imported = 0;
        var createdMaterials = 0;
        var createdLinks = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);
            var supplierCode = Value(values, headers, "suppliercode").Trim();
            var supplierName = Value(values, headers, "suppliername").Trim();
            var materialCode = Value(values, headers, "code").Trim();
            var materialName = Value(values, headers, "name").Trim();
            var partNumber = Value(values, headers, "partnumber").Trim();

            if (string.IsNullOrWhiteSpace(supplierCode) || string.IsNullOrWhiteSpace(supplierName) ||
                string.IsNullOrWhiteSpace(materialCode) || string.IsNullOrWhiteSpace(materialName) ||
                string.IsNullOrWhiteSpace(partNumber))
            {
                continue;
            }

            var supplier = await _dbContext.Suppliers.SingleOrDefaultAsync(item => item.Code == supplierCode, cancellationToken);
            if (supplier is null)
            {
                supplier = new Supplier { Code = supplierCode, Name = supplierName };
                _dbContext.Suppliers.Add(supplier);
            }

            var material = await _dbContext.Materials.SingleOrDefaultAsync(item => item.Code == materialCode, cancellationToken);
            if (material is null)
            {
                material = new Material
                {
                    Code = materialCode,
                    Name = materialName,
                    Unit = Value(values, headers, "unit", "pz").Trim(),
                    Stock = DecimalValue(values, headers, "stock"),
                    MinStock = DecimalValue(values, headers, "minstock")
                };
                _dbContext.Materials.Add(material);
                createdMaterials++;
            }
            else
            {
                material.Name = materialName;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            var link = await _dbContext.MaterialSuppliers.FindAsync(new object[] { material.Id, supplier.Id }, cancellationToken);
            if (link is null)
            {
                link = new MaterialSupplier { MaterialId = material.Id, SupplierId = supplier.Id };
                _dbContext.MaterialSuppliers.Add(link);
                createdLinks++;
            }

            link.PartNumber = partNumber;
            link.Description = Value(values, headers, "description");
            link.UnitPrice = DecimalValue(values, headers, "unitprice");
            link.LeadTimeDays = DecimalValue(values, headers, "leadtimedays");
            imported++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new { imported, createdMaterials, createdLinks });
    }

    private static string Value(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headers, string key, string fallback = "")
    {
        return headers.TryGetValue(key, out var index) && index < values.Count ? values[index] : fallback;
    }

    private static decimal DecimalValue(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headers, string key)
    {
        var value = Value(values, headers, key);
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in line)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }
        values.Add(current.ToString());
        return values;
    }
}
