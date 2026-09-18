using System.Globalization;
using System.Runtime.CompilerServices;
using ClosedXML.Excel;
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
    private static readonly string[] RequiredColumns = ["suppliercode", "suppliername", "code", "name", "partnumber"];

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

        using var reader = new StreamReader(file.OpenReadStream());
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return BadRequest(new { message = "Il CSV non contiene intestazioni." });
        }

        var headers = ParseHeaders(ParseCsvLine(headerLine));
        var headerError = ValidateHeaders(headers);
        if (headerError is not null)
        {
            return BadRequest(new { message = headerError });
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var summary = await ImportRowsAsync(ReadCsvRowsAsync(reader, headers, cancellationToken), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(summary);
    }

    [HttpPost("import-excel")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> ImportExcel(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Selezionare un file Excel (.xlsx) non vuoto." });
        }

        using var workbook = new XLWorkbook(file.OpenReadStream());
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null || worksheet.LastRowUsed() is null)
        {
            return BadRequest(new { message = "Il file Excel non contiene un foglio con dati." });
        }

        var headerRow = worksheet.Row(1);
        var headerValues = headerRow.CellsUsed().Select(cell => cell.GetString()).ToList();
        var headers = ParseHeaders(headerValues);
        var headerError = ValidateHeaders(headers);
        if (headerError is not null)
        {
            return BadRequest(new { message = headerError });
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var summary = await ImportRowsAsync(ReadExcelRows(worksheet, headers), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(summary);
    }

    private static Dictionary<string, int> ParseHeaders(IReadOnlyList<string> headerValues)
    {
        return headerValues
            .Select((value, index) => new { Key = value.Trim().ToLowerInvariant(), index })
            .Where(item => !string.IsNullOrEmpty(item.Key))
            .ToDictionary(item => item.Key, item => item.index);
    }

    private static string? ValidateHeaders(Dictionary<string, int> headers)
    {
        return RequiredColumns.Any(column => !headers.ContainsKey(column))
            ? "Colonne obbligatorie: supplierCode, supplierName, code, name, partNumber."
            : null;
    }

    /// <summary>Applies the supplier/material/catalog-link upsert logic shared by CSV and Excel import,
    /// so both formats stay behaviourally identical.</summary>
    private async Task<ImportSummary> ImportRowsAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, string>> rows,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        var createdMaterials = 0;
        var createdLinks = 0;

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            var supplierCode = row.GetValueOrDefault("suppliercode", "").Trim();
            var supplierName = row.GetValueOrDefault("suppliername", "").Trim();
            var materialCode = row.GetValueOrDefault("code", "").Trim();
            var materialName = row.GetValueOrDefault("name", "").Trim();
            var partNumber = row.GetValueOrDefault("partnumber", "").Trim();

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
                    Unit = ValueOrDefault(row, "unit", "pz").Trim(),
                    Stock = DecimalValue(row, "stock"),
                    MinStock = DecimalValue(row, "minstock")
                };
                _dbContext.Materials.Add(material);
                createdMaterials++;
            }
            else
            {
                material.Name = materialName;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            var link = await _dbContext.MaterialSuppliers.FindAsync([material.Id, supplier.Id], cancellationToken);
            if (link is null)
            {
                link = new MaterialSupplier { MaterialId = material.Id, SupplierId = supplier.Id };
                _dbContext.MaterialSuppliers.Add(link);
                createdLinks++;
            }

            link.PartNumber = partNumber;
            link.Description = row.GetValueOrDefault("description");
            link.UnitPrice = DecimalValue(row, "unitprice");
            link.LeadTimeDays = DecimalValue(row, "leadtimedays");
            imported++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new ImportSummary(imported, createdMaterials, createdLinks);
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, string>> ReadCsvRowsAsync(
        StreamReader reader,
        IReadOnlyDictionary<string, int> headers,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);
            yield return headers.ToDictionary(
                header => header.Key,
                header => header.Value < values.Count ? values[header.Value] : string.Empty);
        }
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, string>> ReadExcelRows(
        IXLWorksheet worksheet,
        IReadOnlyDictionary<string, int> headers)
    {
        var lastRow = worksheet.LastRowUsed()!.RowNumber();
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty())
            {
                continue;
            }

            yield return headers.ToDictionary(
                header => header.Key,
                header => row.Cell(header.Value + 1).GetString());
        }

        await Task.CompletedTask;
    }

    private static string ValueOrDefault(IReadOnlyDictionary<string, string> row, string key, string fallback) =>
        row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static decimal DecimalValue(IReadOnlyDictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var value) &&
               decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
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

public sealed record ImportSummary(int Imported, int CreatedMaterials, int CreatedLinks);
