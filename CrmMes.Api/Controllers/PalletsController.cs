using System.Security.Claims;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Pallet/logistic unit labelling with a GS1-128 SSCC code: useful for coding and shipping in any
/// sector, not just food (it was previously bundled with the food-labels module, which also covers
/// ingredients and allergens — unrelated concerns for a non-food company that just wants to label pallets).</summary>
[ApiController]
[Authorize]
[Route("api/pallets")]
public class PalletsController : ControllerBase
{
    private const int MaxSsccAttempts = 3;
    private readonly ApplicationDbContext _dbContext;

    public PalletsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
            AuditTrail.Add(_dbContext, User, "LogisticUnitCreated", "LogisticUnit", unit.Id, $"Pallet SSCC {unit.Sscc} assegnato.");
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

        AuditTrail.Add(_dbContext, User, "Gs1PrefixSet", "CompanyProfile", company.Id, $"Prefisso aziendale GS1 impostato a {prefix}.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static LogisticUnitResponse ToResponse(LogisticUnit unit) => new(
        unit.Id, unit.Sscc, unit.WorkOrderId, unit.TransportDocumentId, unit.ProductCode, unit.ProductName,
        unit.LotNumber, unit.Quantity, unit.BestBefore, unit.CreatedAt);
}

public sealed record CreateLogisticUnitRequest(Guid? WorkOrderId, Guid? TransportDocumentId, decimal? Quantity);

public sealed record LogisticUnitResponse(
    Guid Id, string Sscc, Guid? WorkOrderId, Guid? TransportDocumentId, string? ProductCode, string? ProductName,
    string? LotNumber, decimal? Quantity, DateTime? BestBefore, DateTime CreatedAt);

public sealed record SetGs1PrefixRequest(string? CompanyPrefix);
