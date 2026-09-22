using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

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

    /// <summary>Best-effort text-table import: works for a PDF whose catalog page is a simple text
    /// table (a header row matching the same required columns as the CSV/Excel import, columns visually
    /// separated by whitespace) — not for scanned/image PDFs (no OCR) or catalogs with a complex graphic
    /// layout (multi-column brochures, merged cells, etc.), which is what most real supplier catalogs
    /// (Schneider, Pizzato...) actually look like. This was built without a real sample catalog to test
    /// against — treat it as a starting point to tune once one is available, not a finished parser.</summary>
    [HttpPost("import-pdf")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> ImportPdf(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Selezionare un file PDF non vuoto." });
        }

        List<string> lines;
        try
        {
            using var stream = file.OpenReadStream();
            lines = ExtractTextLines(stream);
        }
        catch (Exception exception)
        {
            return BadRequest(new { message = $"Impossibile leggere il PDF: {exception.Message}" });
        }

        Dictionary<string, int>? headers = null;
        var headerLineIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var candidate = ParseHeaders(SplitPdfColumns(lines[i]));
            if (RequiredColumns.All(candidate.ContainsKey))
            {
                headers = candidate;
                headerLineIndex = i;
                break;
            }
        }

        if (headers is null)
        {
            return BadRequest(new
            {
                message = "Non è stata trovata una riga di intestazione con le colonne richieste (supplierCode, " +
                    "supplierName, code, name, partNumber) separate da spazi. L'estrazione da PDF funziona solo per " +
                    "tabelle testuali semplici: non per PDF scansionati (serve OCR, non supportato) né per cataloghi " +
                    "con impaginazione grafica complessa."
            });
        }

        var dataLines = lines.Skip(headerLineIndex + 1).Where(line => !string.IsNullOrWhiteSpace(line));

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var summary = await ImportRowsAsync(ReadPdfRows(dataLines, headers), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(summary);
    }

    /// <summary>Reconstructs text lines from a PDF's words, grouping by vertical position (same row) and
    /// ordering by horizontal position within a row, inserting extra spacing where a visual gap between
    /// words is wide enough to likely be a column boundary rather than a word boundary.</summary>
    private static List<string> ExtractTextLines(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);
        var lines = new List<string>();

        foreach (var page in document.GetPages())
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0)
            {
                continue;
            }

            var rows = words
                .GroupBy(word => Math.Round(word.BoundingBox.Bottom / 3) * 3)
                .OrderByDescending(group => group.Key);

            foreach (var row in rows)
            {
                var ordered = row.OrderBy(word => word.BoundingBox.Left).ToList();
                var builder = new System.Text.StringBuilder();
                double? previousRight = null;

                foreach (var word in ordered)
                {
                    if (previousRight.HasValue)
                    {
                        var gap = word.BoundingBox.Left - previousRight.Value;
                        builder.Append(gap > 8 ? "   " : " ");
                    }

                    builder.Append(word.Text);
                    previousRight = word.BoundingBox.Right;
                }

                lines.Add(builder.ToString());
            }
        }

        return lines;
    }

    private static List<string> SplitPdfColumns(string line) =>
        Regex.Split(line.Trim(), @"\s{2,}").Where(value => !string.IsNullOrWhiteSpace(value)).ToList();

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, string>> ReadPdfRows(
        IEnumerable<string> dataLines,
        IReadOnlyDictionary<string, int> headers)
    {
        foreach (var line in dataLines)
        {
            var values = SplitPdfColumns(line);
            yield return headers.ToDictionary(
                header => header.Key,
                header => header.Value < values.Count ? values[header.Value] : string.Empty);
        }

        await Task.CompletedTask;
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
