using System.Security.Claims;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
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
    private const int MaxBomDepth = 8;

    private static readonly string[] DemandStatuses = ["Draft", "Released", "InProgress"];
    private static readonly string[] OpenOrderStatuses = ["Confirmed", "PartiallyReceived"];

    private readonly ApplicationDbContext _db;

    public MrpController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<MrpRunResponse>> Run(CancellationToken cancellationToken = default)
    {
        var result = await BuildRunAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("create-orders")]
    public async Task<ActionResult<CreateMrpOrdersResponse>> CreateOrdersFromSuggestions(
        CreateMrpOrdersRequest request,
        CancellationToken cancellationToken = default)
    {
        var run = await BuildRunAsync(cancellationToken);
        var filter = request.MaterialCodes?
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = run.Suggestions
            .Where(s => s.SuggestedQuantity > 0 && !s.UnknownMaterial && s.PreferredSupplierId.HasValue)
            .Where(s => filter is null || filter.Count == 0 || filter.Contains(s.MaterialCode))
            .ToList();

        var skipped = run.Suggestions
            .Where(s => s.SuggestedQuantity > 0)
            .Where(s => filter is null || filter.Count == 0 || filter.Contains(s.MaterialCode))
            .Where(s => s.UnknownMaterial || !s.PreferredSupplierId.HasValue)
            .Select(s => s.MaterialCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (filter is { Count: > 0 })
        {
            foreach (var code in filter)
            {
                if (!run.Suggestions.Any(s => s.MaterialCode.Equals(code, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped.Add(code);
                }
            }
        }

        var materialIds = await _db.Materials.AsNoTracking()
            .Where(m => candidates.Select(c => c.MaterialCode).Contains(m.Code))
            .ToDictionaryAsync(m => m.Code, m => m.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var supplierLinkRows = materialIds.Count == 0
            ? []
            : await _db.MaterialSuppliers.AsNoTracking()
                .Where(link => materialIds.Values.Contains(link.MaterialId))
                .ToListAsync(cancellationToken);
        var supplierLinks = supplierLinkRows
            .GroupBy(link => link.MaterialId)
            .ToDictionary(g => g.Key, g => g.OrderBy(link => link.UnitPrice).First());

        var created = new List<CreatedMrpOrderSummary>();
        foreach (var group in candidates.GroupBy(s => s.PreferredSupplierId!.Value))
        {
            var supplierId = group.Key;
            if (!await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.IsActive, cancellationToken))
            {
                skipped.AddRange(group.Select(g => g.MaterialCode));
                continue;
            }

            var order = new PurchaseOrder
            {
                Code = $"OD-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                SupplierId = supplierId,
                Status = "Draft"
            };

            DateTime? expectedDelivery = null;
            foreach (var suggestion in group)
            {
                if (!materialIds.TryGetValue(suggestion.MaterialCode, out var materialId)
                    || !supplierLinks.TryGetValue(materialId, out var link))
                {
                    skipped.Add(suggestion.MaterialCode);
                    continue;
                }

                if (suggestion.SuggestedOrderDate is not null && suggestion.LeadTimeDays is not null)
                {
                    var lineDelivery = suggestion.SuggestedOrderDate.Value.Date
                        .AddDays((double)suggestion.LeadTimeDays.Value);
                    expectedDelivery = expectedDelivery is null || lineDelivery > expectedDelivery
                        ? lineDelivery
                        : expectedDelivery;
                }

                var orderItem = new PurchaseOrderItem
                {
                    MaterialCode = suggestion.MaterialCode,
                    Description = suggestion.MaterialName,
                    Quantity = suggestion.SuggestedQuantity,
                    UnitPrice = link.UnitPrice
                };
                _db.PurchaseOrderItems.Add(orderItem);
                order.Items.Add(orderItem);
            }

            if (order.Items.Count == 0)
            {
                continue;
            }

            order.ExpectedDeliveryDate = expectedDelivery;
            _db.PurchaseOrders.Add(order);
            _db.AuditLogs.Add(new AuditLog
            {
                Action = "PurchaseOrderCreated",
                EntityType = "PurchaseOrder",
                EntityId = order.Id,
                UserName = GetCurrentUserName(),
                Details = $"Ordine {order.Code} creato da MRP con {order.Items.Count} righe."
            });

            var supplierName = group.First().SupplierName ?? string.Empty;
            created.Add(new CreatedMrpOrderSummary(order.Id, order.Code, supplierId, supplierName, order.Items.Count));
        }

        await _db.SaveChangesAsync(cancellationToken);

        var skippedDistinct = skipped
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new CreateMrpOrdersResponse(created.Count, created, skippedDistinct));
    }

    private async Task<MrpRunResponse> BuildRunAsync(CancellationToken cancellationToken)
    {
        var productsByCode = await _db.Products.AsNoTracking()
            .Include(p => p.BillOfMaterial)
            .Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var orders = await _db.WorkOrders.AsNoTracking()
            .Include(o => o.Product).ThenInclude(p => p!.BillOfMaterial)
            .Where(o => DemandStatuses.Contains(o.Status) && o.Product != null)
            .OrderBy(o => o.DueDate ?? DateTime.MaxValue)
            .ToListAsync(cancellationToken);

        var demand = new Dictionary<string, MrpDemandAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            ExplodeBom(
                order.Product!.BillOfMaterial,
                order.Quantity,
                order,
                depth: 0,
                productsByCode,
                demand);
        }

        var codes = demand.Keys.ToList();
        var materials = codes.Count == 0
            ? []
            : await _db.Materials.AsNoTracking()
                .Where(m => codes.Contains(m.Code) && m.IsActive)
                .ToListAsync(cancellationToken);
        var byCode = materials.ToDictionary(m => m.Code, StringComparer.OrdinalIgnoreCase);
        var materialIds = materials.Select(m => m.Id).ToList();

        var preferredLinks = materialIds.Count == 0
            ? []
            : await _db.MaterialSuppliers.AsNoTracking()
                .Include(link => link.Supplier)
                .Where(link => materialIds.Contains(link.MaterialId))
                .ToListAsync(cancellationToken);
        var preferredByMaterialId = preferredLinks
            .GroupBy(link => link.MaterialId)
            .ToDictionary(g => g.Key, g => g.OrderBy(link => link.UnitPrice).First());

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

                MaterialSupplier? preferred = null;
                if (material is not null)
                {
                    preferredByMaterialId.TryGetValue(material.Id, out preferred);
                }

                var earliestDue = acc.WorkOrders
                    .Where(w => w.DueDate.HasValue)
                    .Select(w => w.DueDate!.Value)
                    .OrderBy(d => d)
                    .Cast<DateTime?>()
                    .FirstOrDefault();

                DateTime? suggestedOrderDate = null;
                if (earliestDue is not null && preferred is not null)
                {
                    suggestedOrderDate = DateTime.SpecifyKind(
                        earliestDue.Value.Date.AddDays(-(double)preferred.LeadTimeDays),
                        DateTimeKind.Utc);
                }

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
                    preferred?.SupplierId,
                    preferred?.Supplier?.Name,
                    preferred?.LeadTimeDays,
                    suggestedOrderDate,
                    acc.WorkOrders.OrderBy(w => w.DueDate ?? DateTime.MaxValue).ToList());
            })
            .OrderByDescending(s => s.SuggestedQuantity)
            .ThenBy(s => s.MaterialCode)
            .ToList();

        return new MrpRunResponse(
            DateTime.UtcNow,
            orders.Count,
            suggestions.Count,
            suggestions.Count(s => s.SuggestedQuantity > 0),
            suggestions);
    }

    private static void ExplodeBom(
        IEnumerable<BillOfMaterialItem> lines,
        decimal multiplier,
        WorkOrder order,
        int depth,
        IReadOnlyDictionary<string, Product> productsByCode,
        Dictionary<string, MrpDemandAccumulator> demand)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.MaterialCode) || line.Quantity <= 0)
            {
                continue;
            }

            var needed = line.Quantity * multiplier;
            var code = line.MaterialCode.Trim();
            if (depth < MaxBomDepth
                && productsByCode.TryGetValue(code, out var subProduct)
                && subProduct.BillOfMaterial.Count > 0)
            {
                ExplodeBom(subProduct.BillOfMaterial, needed, order, depth + 1, productsByCode, demand);
                continue;
            }

            if (!demand.TryGetValue(code, out var acc))
            {
                acc = new MrpDemandAccumulator(code);
                demand[code] = acc;
            }

            acc.GrossRequirement += needed;
            acc.WorkOrders.Add(new MrpWorkOrderDemand(
                order.Id, order.Code, order.Status, order.DueDate, order.Product!.Code, needed));
        }
    }

    private string? GetCurrentUserName() => User.FindFirstValue(ClaimTypes.Name);

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
    decimal MinStock, bool UnknownMaterial,
    Guid? PreferredSupplierId, string? SupplierName, decimal? LeadTimeDays, DateTime? SuggestedOrderDate,
    IReadOnlyList<MrpWorkOrderDemand> WorkOrders);

public sealed record MrpRunResponse(
    DateTime GeneratedAt, int WorkOrdersConsidered, int Materials, int MaterialsToOrder,
    IReadOnlyList<MrpSuggestionResponse> Suggestions);

public sealed record CreateMrpOrdersRequest(List<string>? MaterialCodes);

public sealed record CreatedMrpOrderSummary(
    Guid Id, string Code, Guid SupplierId, string SupplierName, int ItemCount);

public sealed record CreateMrpOrdersResponse(
    int OrdersCreated, IReadOnlyList<CreatedMrpOrderSummary> Orders, IReadOnlyList<string> SkippedMaterialCodes);
