using CrmMes.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Material requirements planning: explode open work-order bills of materials, subtract stock
/// and quantities already on confirmed purchase orders, and propose what to buy.</summary>
[ApiController]
[Authorize(Policy = "Purchasing")]
[Route("api/procurement/mrp")]
public class MrpController : ControllerBase
{
    private static readonly string[] DemandStatuses = ["Draft", "Released", "InProgress"];
    private static readonly string[] OpenOrderStatuses = ["Confirmed", "PartiallyReceived"];

    private readonly ApplicationDbContext _db;

    public MrpController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<MrpRunResponse>> Run(CancellationToken cancellationToken = default)
    {
        var orders = await _db.WorkOrders.AsNoTracking()
            .Include(o => o.Product).ThenInclude(p => p!.BillOfMaterial)
            .Where(o => DemandStatuses.Contains(o.Status) && o.Product != null)
            .OrderBy(o => o.DueDate ?? DateTime.MaxValue)
            .ToListAsync(cancellationToken);

        var demand = new Dictionary<string, MrpDemandAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            foreach (var line in order.Product!.BillOfMaterial)
            {
                if (string.IsNullOrWhiteSpace(line.MaterialCode) || line.Quantity <= 0)
                {
                    continue;
                }

                var needed = line.Quantity * order.Quantity;
                if (!demand.TryGetValue(line.MaterialCode, out var acc))
                {
                    acc = new MrpDemandAccumulator(line.MaterialCode);
                    demand[line.MaterialCode] = acc;
                }

                acc.GrossRequirement += needed;
                acc.WorkOrders.Add(new MrpWorkOrderDemand(
                    order.Id, order.Code, order.Status, order.DueDate, order.Product!.Code, needed));
            }
        }

        var codes = demand.Keys.ToList();
        var materials = codes.Count == 0
            ? []
            : await _db.Materials.AsNoTracking()
                .Where(m => codes.Contains(m.Code) && m.IsActive)
                .ToListAsync(cancellationToken);
        var byCode = materials.ToDictionary(m => m.Code, StringComparer.OrdinalIgnoreCase);

        var onOrderRows = codes.Count == 0
            ? []
            : await _db.PurchaseOrderItems.AsNoTracking()
                .Where(i => OpenOrderStatuses.Contains(i.PurchaseOrder.Status) && codes.Contains(i.MaterialCode))
                .GroupBy(i => i.MaterialCode)
                .Select(g => new { Code = g.Key, Qty = g.Sum(i => i.Quantity - i.ReceivedQuantity) })
                .ToListAsync(cancellationToken);
        var onOrder = onOrderRows.ToDictionary(x => x.Code, x => Math.Max(0, x.Qty), StringComparer.OrdinalIgnoreCase);

        var suggestions = demand.Values
            .Select(acc =>
            {
                byCode.TryGetValue(acc.MaterialCode, out var material);
                var stock = material?.Stock ?? 0;
                var ordered = onOrder.GetValueOrDefault(acc.MaterialCode);
                var available = stock + ordered;
                var net = acc.GrossRequirement - available;
                var minStock = material?.MinStock ?? 0;
                // Cover the shortage and restore the safety stock when blowing past it.
                var suggested = net > 0
                    ? Math.Max(net, minStock > stock ? minStock - stock : net)
                    : (stock < minStock ? minStock - stock : 0m);
                return new MrpSuggestionResponse(
                    acc.MaterialCode,
                    material?.Name ?? acc.MaterialCode,
                    material?.Unit ?? "pz",
                    Math.Round(acc.GrossRequirement, 4),
                    Math.Round(stock, 4),
                    Math.Round(ordered, 4),
                    Math.Round(Math.Max(0, net), 4),
                    Math.Round(suggested, 4),
                    minStock,
                    material is null,
                    acc.WorkOrders.OrderBy(w => w.DueDate ?? DateTime.MaxValue).ToList());
            })
            .OrderByDescending(s => s.SuggestedQuantity)
            .ThenBy(s => s.MaterialCode)
            .ToList();

        return Ok(new MrpRunResponse(
            DateTime.UtcNow,
            orders.Count,
            suggestions.Count,
            suggestions.Count(s => s.SuggestedQuantity > 0),
            suggestions));
    }

    private sealed class MrpDemandAccumulator(string materialCode)
    {
        public string MaterialCode { get; } = materialCode;
        public decimal GrossRequirement { get; set; }
        public List<MrpWorkOrderDemand> WorkOrders { get; } = [];
    }
}

public sealed record MrpWorkOrderDemand(
    Guid WorkOrderId, string WorkOrderCode, string Status, DateTime? DueDate, string ProductCode, decimal Quantity);

public sealed record MrpSuggestionResponse(
    string MaterialCode, string MaterialName, string Unit,
    decimal GrossRequirement, decimal Stock, decimal OnOrder, decimal NetRequirement, decimal SuggestedQuantity,
    decimal MinStock, bool UnknownMaterial, IReadOnlyList<MrpWorkOrderDemand> WorkOrders);

public sealed record MrpRunResponse(
    DateTime GeneratedAt, int WorkOrdersConsidered, int Materials, int MaterialsToOrder,
    IReadOnlyList<MrpSuggestionResponse> Suggestions);
