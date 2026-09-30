using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Guided recall (richiamo di lotto): starting from a suspect material lot, or from a finished
/// product lot, everything downstream — the work orders that consumed it, the product lots and serials
/// they made, the pallets (SSCC), and the transport documents that took them to customers. Useful in
/// every sector; mandatory in food (Reg. CE 178/2002, art. 18-19).</summary>
[ApiController]
[Authorize]
[Route("api/recall")]
public class RecallController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public RecallController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("material-lot/{lotId:guid}")]
    public async Task<ActionResult<RecallResponse>> FromMaterialLot(Guid lotId, CancellationToken cancellationToken = default)
    {
        var lot = await _dbContext.MaterialLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId, cancellationToken);
        if (lot is null)
        {
            return NotFound();
        }

        // Withdrawals that drew from the lot, and the work orders they fed.
        var usages = await (
            from consumption in _dbContext.MaterialLotConsumptions.AsNoTracking()
            where consumption.MaterialLotId == lotId
            join item in _dbContext.WithdrawalItems.AsNoTracking() on consumption.WithdrawalItemId equals item.Id
            join slip in _dbContext.WithdrawalSlips.AsNoTracking() on item.WithdrawalSlipId equals slip.Id
            select new { consumption.Quantity, slip.WorkOrderId, SlipCode = slip.Code })
            .ToListAsync(cancellationToken);

        var consumedByOrder = usages.Where(u => u.WorkOrderId.HasValue)
            .GroupBy(u => u.WorkOrderId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(u => u.Quantity));

        // Serials that received the lot directly (per-unit genealogy), in case they weren't all affected.
        var affectedUnits = await _dbContext.WorkOrderUnitMaterialLots.AsNoTracking()
            .Where(link => link.MaterialLotId == lotId)
            .Select(link => new { link.Unit.WorkOrderId, link.Unit.SerialNumber })
            .ToListAsync(cancellationToken);
        foreach (var unit in affectedUnits.Where(unit => !consumedByOrder.ContainsKey(unit.WorkOrderId)))
        {
            consumedByOrder[unit.WorkOrderId] = 0;
        }

        var outsideOrders = usages.Where(u => u.WorkOrderId is null).Select(u => u.SlipCode).Distinct().ToList();
        var result = await BuildAsync(
            $"Lotto materiale {lot.LotNumber} ({lot.MaterialCode})", consumedByOrder,
            affectedUnits.ToLookup(u => u.WorkOrderId, u => u.SerialNumber),
            directLotNumber: lot.LotNumber, directMaterialCode: lot.MaterialCode, cancellationToken);

        var warnings = result.Warnings.ToList();
        if (outsideOrders.Count > 0)
        {
            warnings.Add($"Parte del lotto è uscita con distinte non legate a una commessa ({string.Join(", ", outsideOrders)}): verifica a mano dove è finita.");
        }

        if (lot.Quantity > 0)
        {
            warnings.Add($"Ancora in magazzino: {lot.Quantity:0.###} del lotto, da bloccare.");
        }

        return Ok(result with { Warnings = warnings });
    }

    [HttpGet("product-lot")]
    public async Task<ActionResult<RecallResponse>> FromProductLot([FromQuery] string lotNumber, CancellationToken cancellationToken = default)
    {
        var lot = lotNumber?.Trim();
        if (string.IsNullOrEmpty(lot))
        {
            return BadRequest(new { message = "Indica il lotto del prodotto." });
        }

        var orders = await _dbContext.WorkOrders.AsNoTracking()
            .Where(o => o.ProductLotNumber == lot)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return NotFound(new { message = "Nessuna commessa con questo lotto prodotto." });
        }

        return Ok(await BuildAsync($"Lotto prodotto {lot}", orders.ToDictionary(id => id, _ => 0m),
            Enumerable.Empty<(Guid, string)>().ToLookup(x => x.Item1, x => x.Item2), null, null, cancellationToken));
    }

    private async Task<RecallResponse> BuildAsync(
        string subject, Dictionary<Guid, decimal> consumedByOrder, ILookup<Guid, string> unitsByOrder,
        string? directLotNumber, string? directMaterialCode, CancellationToken cancellationToken)
    {
        var orderIds = consumedByOrder.Keys.ToList();
        var orders = await _dbContext.WorkOrders.AsNoTracking()
            .Include(o => o.Product)
            .Include(o => o.Customer)
            .Where(o => orderIds.Contains(o.Id))
            .ToListAsync(cancellationToken);
        var productLots = orders.Select(o => o.ProductLotNumber).Where(l => !string.IsNullOrEmpty(l)).Distinct().ToList();

        // Shipments: issued DDT lines that carry one of these jobs, one of their product lots, or the
        // material lot itself (resold or sent to a subcontractor as is).
        var documents = await _dbContext.TransportDocuments.AsNoTracking()
            .Include(d => d.Customer)
            .Include(d => d.Lines)
            .Where(d => d.Status == "Issued" &&
                        ((d.WorkOrderId != null && orderIds.Contains(d.WorkOrderId.Value)) ||
                         d.Lines.Any(l => l.LotNumber != null &&
                                          (productLots.Contains(l.LotNumber) || (directLotNumber != null && l.LotNumber == directLotNumber)))))
            .ToListAsync(cancellationToken);

        var shipments = new List<RecallShipment>();
        foreach (var document in documents.OrderBy(d => d.IssuedAt))
        {
            var lines = document.Lines.Where(l =>
                (l.LotNumber != null && (productLots.Contains(l.LotNumber) || l.LotNumber == directLotNumber)) ||
                (document.WorkOrderId != null && orderIds.Contains(document.WorkOrderId.Value) && l.ProductId != null));
            foreach (var line in lines)
            {
                if (line.LotNumber == directLotNumber && directMaterialCode is not null && line.Code is not null &&
                    !string.Equals(line.Code, directMaterialCode, StringComparison.OrdinalIgnoreCase) && !productLots.Contains(line.LotNumber!))
                {
                    continue; // same lot number, different material: not ours
                }

                shipments.Add(new RecallShipment(
                    document.Id, $"{document.Number}/{document.Year}", document.IssuedAt, document.RecipientName,
                    document.Customer?.Code, document.RecipientAddress, line.Code, line.Description, line.Quantity, line.Unit, line.LotNumber));
            }
        }

        var pallets = await _dbContext.LogisticUnits.AsNoTracking()
            .Where(u => (u.WorkOrderId != null && orderIds.Contains(u.WorkOrderId.Value)) ||
                        (u.LotNumber != null && productLots.Contains(u.LotNumber)))
            .OrderBy(u => u.CreatedAt)
            .Select(u => new RecallPallet(u.Sscc, u.LotNumber, u.Quantity, u.CreatedAt))
            .ToListAsync(cancellationToken);

        var shippedOrders = documents.Where(d => d.WorkOrderId != null).Select(d => d.WorkOrderId!.Value)
            .Concat(orders.Where(o => shipments.Any(s => s.LotNumber == o.ProductLotNumber)).Select(o => o.Id))
            .ToHashSet();
        var warnings = new List<string>();
        var unshippedDone = orders.Where(o => o.Status == "Completed" && !shippedOrders.Contains(o.Id)).Select(o => o.Code).ToList();
        if (unshippedDone.Count > 0)
        {
            warnings.Add($"Commesse completate senza DDT registrato ({string.Join(", ", unshippedDone)}): la merce può essere ancora in magazzino o uscita senza DDT.");
        }

        var customers = shipments.Select(s => s.RecipientName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
        return new RecallResponse(
            subject,
            orders.OrderBy(o => o.CreatedAt).Select(o => new RecallWorkOrder(
                o.Id, o.Code, o.Product.Code, o.Product.Name, o.ProductLotNumber, o.Quantity, o.Status,
                consumedByOrder.GetValueOrDefault(o.Id), o.Customer?.Name ?? o.CustomerReference,
                unitsByOrder[o.Id].OrderBy(s => s).ToList())).ToList(),
            pallets, shipments, customers, warnings);
    }
}

public sealed record RecallWorkOrder(
    Guid Id, string Code, string ProductCode, string ProductName, string? ProductLotNumber, decimal Quantity,
    string Status, decimal ConsumedQuantity, string? CustomerName, List<string> AffectedSerials);

public sealed record RecallShipment(
    Guid DocumentId, string DocumentCode, DateTime? IssuedAt, string RecipientName, string? CustomerCode,
    string? RecipientAddress, string? Code, string Description, decimal Quantity, string Unit, string? LotNumber);

public sealed record RecallPallet(string Sscc, string? LotNumber, decimal? Quantity, DateTime CreatedAt);

public sealed record RecallResponse(
    string Subject, List<RecallWorkOrder> WorkOrders, List<RecallPallet> Pallets, List<RecallShipment> Shipments,
    List<string> Customers, List<string> Warnings);
