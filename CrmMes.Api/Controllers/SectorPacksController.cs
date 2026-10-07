using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Pacchetti settore G13: catalogo, anteprime e import/operazioni persistenti.</summary>
[ApiController]
[Authorize]
[Route("api/sector-packs")]
public class SectorPacksController(ApplicationDbContext db) : ControllerBase
{
    private const string EngineeringPolicy = "Engineering";
    private const string WarehousePolicy = "Warehouse";
    private const string SalesPolicy = "Sales";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [HttpGet]
    public ActionResult<IEnumerable<SectorPackSummaryResponse>> List() =>
        Ok(SectorPacks.All.Select(ToSummary));

    [HttpGet("{key}")]
    public ActionResult<SectorPackDetailResponse> GetByKey(string key)
    {
        var pack = SectorPacks.Find(key);
        return pack is null ? NotFound(new { message = "Pacchetto settore non trovato." }) : Ok(ToDetail(pack));
    }

    [Authorize(Policy = EngineeringPolicy)]
    [HttpPost("eplan-bom/preview")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackPreviewResponse<EplanBomRow>>> PreviewEplanBom(CancellationToken cancellationToken) =>
        await PreviewAsync(SectorPacks.ParseEplanBom, cancellationToken);

    [Authorize(Policy = EngineeringPolicy)]
    [HttpPost("eplan-bom/import")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackImportResponse>> ImportEplanBom(
        [FromQuery] Guid productId,
        CancellationToken cancellationToken)
    {
        var text = await ReadImportTextAsync(cancellationToken);
        if (text is null)
        {
            return BadRequest(new { message = "Invia il contenuto come testo (text/csv o text/plain) o come file nel multipart." });
        }

        var parsed = SectorPacks.ParseEplanBom(text);
        if (parsed.Rows.Count == 0)
        {
            return BadRequest(new { message = "Nessuna riga valida da importare.", errors = parsed.Errors.Take(20) });
        }

        var product = await db.Products
            .Include(p => p.BillOfMaterial)
            .Include(p => p.RoutingSteps)
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return NotFound(new { message = "Prodotto non trovato." });
        }

        var materialsCreated = 0;
        foreach (var row in parsed.Rows)
        {
            var code = row.PartNumber.Trim();
            var name = string.IsNullOrWhiteSpace(row.Description) ? code : row.Description.Trim();
            if (await EnsureMaterialExistsAsync(code, name, cancellationToken))
            {
                materialsCreated++;
            }
        }

        foreach (var item in product.BillOfMaterial.ToList())
        {
            product.BillOfMaterial.Remove(item);
        }

        foreach (var row in parsed.Rows)
        {
            db.BillOfMaterialItems.Add(new BillOfMaterialItem
            {
                ProductId = product.Id,
                MaterialCode = row.PartNumber.Trim(),
                Quantity = row.Quantity,
                Notes = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim()
            });
        }

        AuditTrail.Add(db, User, "EplanBomImported", "Product", product.Id, $"Distinta da EPLAN importata su {product.Code}: {parsed.Rows.Count} righe.");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new SectorPackImportResponse(parsed.Rows.Count, materialsCreated, parsed.Errors.Count, parsed.Errors.Take(50).ToList()));
    }

    [Authorize(Policy = EngineeringPolicy)]
    [HttpPost("wire-list/preview")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackPreviewResponse<WireListRow>>> PreviewWireList(CancellationToken cancellationToken) =>
        await PreviewAsync(SectorPacks.ParseWireList, cancellationToken);

    [Authorize(Policy = EngineeringPolicy)]
    [HttpPost("wire-list/import")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackImportResponse>> ImportWireList(
        [FromQuery] Guid productId,
        CancellationToken cancellationToken)
    {
        var text = await ReadImportTextAsync(cancellationToken);
        if (text is null)
        {
            return BadRequest(new { message = "Invia il contenuto come testo (text/csv o text/plain) o come file nel multipart." });
        }

        var parsed = SectorPacks.ParseWireList(text);
        if (parsed.Rows.Count == 0)
        {
            return BadRequest(new { message = "Nessuna riga valida da importare.", errors = parsed.Errors.Take(20) });
        }

        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            return NotFound(new { message = "Prodotto non trovato." });
        }

        var body = new StringBuilder();
        body.AppendLine("From;To;Section;Color");
        foreach (var row in parsed.Rows)
        {
            body.AppendLine($"{row.From};{row.To};{row.Section};{row.Color}");
        }

        await UpsertTextDocumentAsync(productId, "Lista cavi", "wire-list.txt", body.ToString(), cancellationToken);
        AuditTrail.Add(db, User, "WireListImported", "Product", productId, $"Lista cavi importata: {parsed.Rows.Count} righe.");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new SectorPackImportResponse(parsed.Rows.Count, 0, parsed.Errors.Count, parsed.Errors.Take(50).ToList()));
    }

    [Authorize(Policy = EngineeringPolicy)]
    [HttpPost("nutrition-table/preview")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackPreviewResponse<NutritionTableRow>>> PreviewNutritionTable(CancellationToken cancellationToken) =>
        await PreviewAsync(SectorPacks.ParseNutritionTable, cancellationToken);

    [Authorize(Policy = EngineeringPolicy)]
    [HttpPost("nutrition-table/import")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackImportResponse>> ImportNutritionTable(
        [FromQuery] Guid productId,
        CancellationToken cancellationToken)
    {
        var text = await ReadImportTextAsync(cancellationToken);
        if (text is null)
        {
            return BadRequest(new { message = "Invia il contenuto come testo (text/csv o text/plain) o come file nel multipart." });
        }

        var parsed = SectorPacks.ParseNutritionTable(text);
        if (parsed.Rows.Count == 0)
        {
            return BadRequest(new { message = "Nessuna riga valida da importare.", errors = parsed.Errors.Take(20) });
        }

        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            return NotFound(new { message = "Prodotto non trovato." });
        }

        var json = JsonSerializer.Serialize(
            parsed.Rows.Select(r => new { nutrient = r.Nutrient, per100g = r.Per100g }),
            JsonOptions);
        await UpsertTextDocumentAsync(productId, "Tabella nutrizionale", "nutrition.json", json, cancellationToken);
        AuditTrail.Add(db, User, "NutritionTableImported", "Product", productId, $"Tabella nutrizionale importata: {parsed.Rows.Count} righe.");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new SectorPackImportResponse(parsed.Rows.Count, 0, parsed.Errors.Count, parsed.Errors.Take(50).ToList()));
    }

    [Authorize(Policy = EngineeringPolicy)]
    [HttpPost("dm3708/preview")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackPreviewResponse<Dm3708Row>>> PreviewDm3708(CancellationToken cancellationToken) =>
        await PreviewAsync(SectorPacks.ParseDm3708, cancellationToken);

    [Authorize(Policy = EngineeringPolicy)]
    [HttpPost("dm3708/generate")]
    public ActionResult GenerateDm3708([FromBody] Dm3708DeclarationInfo request)
    {
        if (string.IsNullOrWhiteSpace(request.PlantCode))
        {
            return BadRequest(new { message = "Codice impianto obbligatorio." });
        }

        var text = SectorPacks.BuildDm3708Declaration(request);
        var bytes = Encoding.UTF8.GetBytes(text);
        return File(bytes, "text/plain; charset=utf-8", "Dichiarazione-conformita-DM-37-08.txt");
    }

    [Authorize(Policy = SalesPolicy)]
    [HttpPost("sal/preview")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackPreviewResponse<SalRow>>> PreviewSal(CancellationToken cancellationToken) =>
        await PreviewAsync(SectorPacks.ParseSal, cancellationToken);

    [Authorize(Policy = SalesPolicy)]
    [HttpPost("sal/apply")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackApplyResponse>> ApplySal(CancellationToken cancellationToken)
    {
        var text = await ReadImportTextAsync(cancellationToken);
        if (text is null)
        {
            return BadRequest(new { message = "Invia il contenuto come testo (text/csv o text/plain) o come file nel multipart." });
        }

        var parsed = SectorPacks.ParseSal(text);
        if (parsed.Rows.Count == 0)
        {
            return BadRequest(new { message = "Nessuna riga valida da applicare.", errors = parsed.Errors.Take(20) });
        }

        var applied = 0;
        var skipped = new List<SectorPackLineIssue>();
        foreach (var row in parsed.Rows)
        {
            var order = await db.WorkOrders
                .AsNoTracking()
                .SingleOrDefaultAsync(w => w.Code == row.WorkOrderCode, cancellationToken);
            if (order is null)
            {
                skipped.Add(new SectorPackLineIssue(row.Row, $"Commessa «{row.WorkOrderCode}» non trovata."));
                continue;
            }

            db.ProgressCertificates.Add(new ProgressCertificate
            {
                WorkOrderId = order.Id,
                PercentComplete = row.PercentComplete,
                Amount = row.Amount,
                Notes = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes.Trim()
            });
            applied++;
        }

        AuditTrail.Add(db, User, "SalApplied", "SAL", null, $"SAL applicato: {applied} commesse, {skipped.Count} scartate.");
        await db.SaveChangesAsync(cancellationToken);
        var issues = parsed.Errors.Concat(skipped).Take(50).ToList();
        return Ok(new SectorPackApplyResponse(applied, skipped.Count, issues, parsed.Errors.Count + skipped.Count));
    }

    [Authorize(Policy = EngineeringPolicy)]
    [HttpPost("crew-calendar/preview")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackPreviewResponse<CrewCalendarRow>>> PreviewCrewCalendar(CancellationToken cancellationToken) =>
        await PreviewAsync(SectorPacks.ParseCrewCalendar, cancellationToken);

    [Authorize(Policy = WarehousePolicy)]
    [HttpPost("cert-31/preview")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackPreviewResponse<Cert31Row>>> PreviewCert31(CancellationToken cancellationToken) =>
        await PreviewAsync(SectorPacks.ParseCert31, cancellationToken);

    [Authorize(Policy = WarehousePolicy)]
    [HttpPost("cert-31/apply")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackApplyResponse>> ApplyCert31(CancellationToken cancellationToken)
    {
        var text = await ReadImportTextAsync(cancellationToken);
        if (text is null)
        {
            return BadRequest(new { message = "Invia il contenuto come testo (text/csv o text/plain) o come file nel multipart." });
        }

        var parsed = SectorPacks.ParseCert31(text);
        if (parsed.Rows.Count == 0)
        {
            return BadRequest(new { message = "Nessuna riga valida da applicare.", errors = parsed.Errors.Take(20) });
        }

        var applied = 0;
        var skipped = new List<SectorPackLineIssue>();
        foreach (var row in parsed.Rows)
        {
            var lot = await db.MaterialLots
                .SingleOrDefaultAsync(
                    l => l.LotNumber == row.LotNumber && l.MaterialCode == row.MaterialCode,
                    cancellationToken);
            if (lot is null)
            {
                skipped.Add(new SectorPackLineIssue(row.Row, $"Lotto «{row.LotNumber}» del materiale «{row.MaterialCode}» non trovato."));
                continue;
            }

            lot.CertificateNumber = row.CertificateNumber;
            lot.CertificateIssuer = string.IsNullOrWhiteSpace(row.Issuer) ? null : row.Issuer.Trim();
            lot.CertificateIssuedOn = row.IssuedOn;
            applied++;
        }

        AuditTrail.Add(db, User, "Cert31Applied", "Cert31", null, $"Certificati 3.1 applicati: {applied} lotti, {skipped.Count} scartati.");
        await db.SaveChangesAsync(cancellationToken);
        var issues = parsed.Errors.Concat(skipped).Take(50).ToList();
        return Ok(new SectorPackApplyResponse(applied, skipped.Count, issues, parsed.Errors.Count + skipped.Count));
    }

    [Authorize(Policy = WarehousePolicy)]
    [HttpPost("scales/preview")]
    [Consumes("text/csv", "text/plain", "multipart/form-data")]
    public async Task<ActionResult<SectorPackPreviewResponse<ScaleReadingRow>>> PreviewScales(CancellationToken cancellationToken) =>
        await PreviewAsync(SectorPacks.ParseScaleReadings, cancellationToken);

    [Authorize(Policy = WarehousePolicy)]
    [HttpPost("scales/reading")]
    public async Task<ActionResult<ScaleReadingResponse>> RecordScaleReading(
        [FromBody] ScaleReadingRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MaterialCode))
        {
            return BadRequest(new { message = "Codice materiale obbligatorio." });
        }

        if (request.WeightKg <= 0)
        {
            return BadRequest(new { message = "Peso non valido." });
        }

        var reading = new ScaleReading
        {
            MaterialCode = request.MaterialCode.Trim(),
            WeightKg = request.WeightKg,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "kg" : request.Unit.Trim(),
            RecordedAt = request.RecordedAt ?? DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        db.ScaleReadings.Add(reading);
        AuditTrail.Add(db, User, "ScaleReadingRecorded", "ScaleReading", reading.Id, $"Lettura bilancia registrata per {reading.MaterialCode}.");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new ScaleReadingResponse(reading.Id, reading.MaterialCode, reading.WeightKg, reading.Unit, reading.RecordedAt, reading.Notes));
    }

    private async Task<ActionResult<SectorPackPreviewResponse<TRow>>> PreviewAsync<TRow>(
        Func<string, SectorPackParseResult<TRow>> parse,
        CancellationToken cancellationToken)
    {
        var text = await ReadImportTextAsync(cancellationToken);
        if (text is null)
        {
            return BadRequest(new { message = "Invia il contenuto come testo (text/csv o text/plain) o come file nel multipart." });
        }

        return Ok(ToPreview(parse(text)));
    }

    /// <returns>true se il materiale è stato creato ex novo.</returns>
    private async Task<bool> EnsureMaterialExistsAsync(string code, string name, CancellationToken cancellationToken)
    {
        var material = await db.Materials.SingleOrDefaultAsync(m => m.Code == code, cancellationToken);
        if (material is null)
        {
            db.Materials.Add(new Material
            {
                Code = code,
                Name = name,
                Unit = "pz",
                IsActive = true
            });
            return true;
        }

        if (!material.IsActive)
        {
            material.IsActive = true;
        }

        if (string.IsNullOrWhiteSpace(material.Name) && !string.IsNullOrWhiteSpace(name))
        {
            material.Name = name;
        }

        return false;
    }

    private async Task UpsertTextDocumentAsync(
        Guid productId,
        string title,
        string fileName,
        string textContent,
        CancellationToken cancellationToken)
    {
        var data = Encoding.UTF8.GetBytes(textContent);
        var previous = await db.TechnicalDocuments
            .Where(d => d.ProductId == productId && d.IsCurrent && d.Title == title && d.StepSequence == null)
            .ToListAsync(cancellationToken);
        foreach (var old in previous)
        {
            old.IsCurrent = false;
        }

        var lastVersion = await db.TechnicalDocuments
            .Where(d => d.ProductId == productId && d.Title == title && d.StepSequence == null)
            .MaxAsync(d => (int?)d.Version, cancellationToken) ?? 0;

        var document = new TechnicalDocument
        {
            ProductId = productId,
            Kind = "other",
            Title = title,
            FileName = fileName,
            ContentType = "text/plain; charset=utf-8",
            SizeBytes = data.LongLength,
            Version = lastVersion + 1,
            UploadedBy = User.Identity?.Name,
            Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
        };
        db.TechnicalDocuments.Add(document);
        db.TechnicalDocumentContents.Add(new TechnicalDocumentContent { TechnicalDocumentId = document.Id, Data = data });
    }

    private static SectorPackSummaryResponse ToSummary(SectorPackDefinition pack) =>
        new(pack.Key, pack.Name, pack.Description, pack.Sector, pack.Available);

    private static SectorPackDetailResponse ToDetail(SectorPackDefinition pack) =>
        new(pack.Key, pack.Name, pack.Description, pack.Sector, pack.Available);

    private static SectorPackPreviewResponse<TRow> ToPreview<TRow>(SectorPackParseResult<TRow> parsed) =>
        new(
            parsed.Rows.Count,
            parsed.Rows.Take(SectorPacks.MaxPreviewSamples).ToList(),
            parsed.Errors.Take(50).ToList(),
            parsed.Errors.Count);

    private async Task<string?> ReadImportTextAsync(CancellationToken cancellationToken)
    {
        if (Request.HasFormContentType)
        {
            var file = Request.Form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return null;
            }

            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        if (Request.ContentLength is 0 or null && !Request.Body.CanRead)
        {
            return null;
        }

        using var bodyReader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await bodyReader.ReadToEndAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

public sealed record SectorPackSummaryResponse(string Key, string Name, string Description, string Sector, bool Available);

public sealed record SectorPackDetailResponse(string Key, string Name, string Description, string Sector, bool Available);

public sealed record SectorPackPreviewResponse<TRow>(
    int RowCount,
    IReadOnlyList<TRow> Samples,
    IReadOnlyList<SectorPackLineIssue> Errors,
    int ErrorCount);

public sealed record SectorPackImportResponse(
    int ImportedCount,
    int MaterialsCreated,
    int ErrorCount,
    IReadOnlyList<SectorPackLineIssue> Errors);

public sealed record SectorPackApplyResponse(
    int AppliedCount,
    int SkippedCount,
    IReadOnlyList<SectorPackLineIssue> Issues,
    int ErrorCount);

public sealed record ScaleReadingRequest(string MaterialCode, decimal WeightKg, string? Unit, DateTime? RecordedAt, string? Notes);

public sealed record ScaleReadingResponse(Guid Id, string MaterialCode, decimal WeightKg, string Unit, DateTime RecordedAt, string? Notes);
