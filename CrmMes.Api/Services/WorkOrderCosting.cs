using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Services;

/// <summary>Costo di commessa: stimato (distinta base e ciclo, come in preventivo) contro reale
/// (materiali davvero prelevati e minuti davvero lavorati), e margine sul prezzo di vendita.
///
/// Materiali reali: le righe delle distinte di prelievo chiuse della commessa, valorizzate al prezzo
/// d'acquisto effettivo del lotto consumato (riga dell'ordine fornitore che l'ha caricato) e, per la
/// parte senza lotto o con lotto senza ordine, al prezzo più basso a listino.
/// Manodopera reale: durata avvio-fine di ogni fase (fino ad ora se ancora in corso) più le ore
/// registrate a mano, per la tariffa oraria del centro di lavoro.
///
/// Un dato mancante (prezzo, tariffa, prezzo di vendita) non vale mai zero in silenzio: è riportato in
/// Warnings, così un costo incompleto non passa per un margine alto.</summary>
public sealed class WorkOrderCosting
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MaterialPricing _materialPricing;

    public WorkOrderCosting(ApplicationDbContext dbContext, MaterialPricing materialPricing)
    {
        _dbContext = dbContext;
        _materialPricing = materialPricing;
    }

    public async Task<WorkOrderCostingResult?> CalculateAsync(Guid workOrderId, DateTime now, CancellationToken cancellationToken) =>
        (await CalculateManyAsync([workOrderId], now, cancellationToken)).FirstOrDefault();

    /// <summary>Costs several work orders with a fixed number of queries (7), whatever their count: the
    /// margin overview used to run ~7 queries per work order, seconds for a 50-row page on Neon.
    /// Results come back in the order of <paramref name="workOrderIds"/>; unknown ids are skipped.</summary>
    public async Task<List<WorkOrderCostingResult>> CalculateManyAsync(
        IReadOnlyCollection<Guid> workOrderIds, DateTime now, CancellationToken cancellationToken)
    {
        var ids = workOrderIds.Distinct().ToList();
        var orders = await _dbContext.WorkOrders.AsNoTracking()
            .Include(o => o.Operations)
            .Include(o => o.Product).ThenInclude(p => p.BillOfMaterial)
            .Where(o => ids.Contains(o.Id))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return [];
        }

        var workCenters = await _dbContext.WorkCenters.AsNoTracking().ToListAsync(cancellationToken);

        var slipItems = await _dbContext.WithdrawalItems.AsNoTracking()
            .Where(item => item.WithdrawalSlip.WorkOrderId != null && ids.Contains(item.WithdrawalSlip.WorkOrderId.Value) &&
                           item.WithdrawalSlip.Status == "Closed")
            .Select(item => new { Item = item, WorkOrderId = item.WithdrawalSlip.WorkOrderId!.Value })
            .ToListAsync(cancellationToken);
        var itemIds = slipItems.Select(entry => entry.Item.Id).ToList();
        var consumptions = await _dbContext.MaterialLotConsumptions.AsNoTracking()
            .Include(c => c.MaterialLot)
            .Where(c => itemIds.Contains(c.WithdrawalItemId))
            .ToListAsync(cancellationToken);

        var purchaseOrderIds = consumptions.Select(c => c.MaterialLot.PurchaseOrderId).OfType<Guid>().Distinct().ToList();
        var purchaseLines = await _dbContext.PurchaseOrderItems.AsNoTracking()
            .Where(line => purchaseOrderIds.Contains(line.PurchaseOrderId) && line.UnitPrice > 0)
            .Select(line => new { line.PurchaseOrderId, line.MaterialCode, line.UnitPrice })
            .ToListAsync(cancellationToken);
        decimal? LotUnitCost(MaterialLot lot) => lot.PurchaseOrderId is { } poId
            ? purchaseLines.FirstOrDefault(line => line.PurchaseOrderId == poId &&
                string.Equals(line.MaterialCode, lot.MaterialCode, StringComparison.OrdinalIgnoreCase))?.UnitPrice
            : null;

        var catalogPrices = await _materialPricing.CheapestCatalogPricesAsync(
            slipItems.Select(entry => entry.Item.MaterialCode)
                .Concat(orders.SelectMany(o => o.Product.BillOfMaterial).Select(b => b.MaterialCode)),
            cancellationToken);

        var entries = await _dbContext.LaborEntries.AsNoTracking()
            .Include(e => e.WorkCenter)
            .Include(e => e.Operation)
            .Where(e => ids.Contains(e.WorkOrderId))
            .OrderBy(e => e.WorkDate)
            .ToListAsync(cancellationToken);

        var consumptionsByItem = consumptions.ToLookup(c => c.WithdrawalItemId);
        var itemsByOrder = slipItems.ToLookup(entry => entry.WorkOrderId, entry => entry.Item);
        var entriesByOrder = entries.ToLookup(e => e.WorkOrderId);
        var ordersById = orders.ToDictionary(o => o.Id);

        return ids
            .Where(ordersById.ContainsKey)
            .Select(id => Compute(
                ordersById[id], itemsByOrder[id].ToList(), entriesByOrder[id].ToList(),
                workCenters, consumptionsByItem, LotUnitCost, catalogPrices, now))
            .ToList();
    }

    private static WorkOrderCostingResult Compute(
        WorkOrder order,
        List<WithdrawalItem> slipItems,
        List<LaborEntry> entries,
        List<WorkCenter> workCenters,
        ILookup<Guid, MaterialLotConsumption> consumptionsByItem,
        Func<MaterialLot, decimal?> lotUnitCost,
        IReadOnlyDictionary<string, decimal> catalogPrices,
        DateTime now)
    {
        var warnings = new List<string>();
        WorkCenter? FindWorkCenter(string? name) => string.IsNullOrWhiteSpace(name)
            ? null
            : workCenters.FirstOrDefault(w =>
                string.Equals(w.Name, name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(w.Code, name.Trim(), StringComparison.OrdinalIgnoreCase));
        var workCentersWithoutRate = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        decimal? CatalogPrice(string code) => catalogPrices.TryGetValue(code, out var price) ? price : null;

        // --- Materials, actual ---------------------------------------------------------------------
        var materialLines = new List<MaterialCostLine>();
        foreach (var group in slipItems.GroupBy(item => item.MaterialCode, StringComparer.OrdinalIgnoreCase))
        {
            decimal quantity = 0, cost = 0, unpricedQuantity = 0;
            var fromPurchase = false;
            var fromCatalog = false;
            foreach (var item in group)
            {
                quantity += item.Quantity;
                var remaining = item.Quantity;
                foreach (var consumption in consumptionsByItem[item.Id])
                {
                    if (lotUnitCost(consumption.MaterialLot) is { } lotCost)
                    {
                        cost += consumption.Quantity * lotCost;
                        remaining -= consumption.Quantity;
                        fromPurchase = true;
                    }
                }

                if (remaining > 0)
                {
                    if (CatalogPrice(item.MaterialCode) is { } catalog)
                    {
                        cost += remaining * catalog;
                        fromCatalog = true;
                    }
                    else
                    {
                        unpricedQuantity += remaining;
                    }
                }
            }

            var source = (fromPurchase, fromCatalog) switch
            {
                (true, true) => "Acquisto + listino",
                (true, false) => "Prezzo d'acquisto",
                (false, true) => "Listino",
                _ => "Senza prezzo"
            };
            materialLines.Add(new MaterialCostLine(group.Key, quantity, Round(cost), source, unpricedQuantity));
        }

        var unpricedMaterials = materialLines.Count(line => line.UnpricedQuantity > 0);
        if (unpricedMaterials > 0)
        {
            warnings.Add($"{unpricedMaterials} materiali prelevati senza prezzo d'acquisto né a listino: il costo materiali reale è sottostimato.");
        }

        if (slipItems.Count == 0)
        {
            warnings.Add("Nessuna distinta di prelievo chiusa per questa commessa: il costo materiali reale è ancora zero.");
        }

        // --- Labor, actual -------------------------------------------------------------------------
        var laborLines = new List<LaborCostLine>();
        var longPhases = 0;
        foreach (var operation in order.Operations.Where(op => op.StartedAt.HasValue).OrderBy(op => op.SequenceNumber))
        {
            var end = operation.CompletedAt ?? now;
            var minutes = (decimal)Math.Max(0, (end - operation.StartedAt!.Value).TotalMinutes);
            if (minutes > LongPhaseMinutes)
            {
                longPhases++;
            }
            var workCenter = FindWorkCenter(operation.WorkCenter);
            var rate = workCenter?.HourlyRate;
            if (rate is null)
            {
                workCentersWithoutRate.Add(operation.WorkCenter ?? "(fase senza centro di lavoro)");
            }

            laborLines.Add(new LaborCostLine(
                "Fase", $"{operation.SequenceNumber}. {operation.Name}", operation.WorkCenter,
                operation.CompletedBy ?? operation.StartedBy, Round(minutes), rate,
                rate is { } r ? Round(minutes / 60 * r) : null, operation.CompletedAt is null, null));
        }

        foreach (var entry in entries)
        {
            var workCenter = entry.WorkCenter ?? FindWorkCenter(entry.Operation?.WorkCenter);
            var rate = workCenter?.HourlyRate;
            if (rate is null)
            {
                workCentersWithoutRate.Add(workCenter?.Name ?? "(ore senza centro di lavoro)");
            }

            laborLines.Add(new LaborCostLine(
                "Ore registrate", entry.Notes ?? entry.Operation?.Name ?? "Ore registrate", workCenter?.Name,
                entry.OperatorName, entry.Minutes, rate, rate is { } r ? Round(entry.Minutes / 60 * r) : null, false, entry.Id));
        }

        // --- Estimate (same basis as a quote) ------------------------------------------------------
        decimal estimatedMaterial = 0;
        var estimateUnpriced = 0;
        foreach (var bomLine in order.Product.BillOfMaterial)
        {
            if (CatalogPrice(bomLine.MaterialCode) is { } price)
            {
                estimatedMaterial += bomLine.Quantity * order.Quantity * price;
            }
            else
            {
                estimateUnpriced++;
            }
        }

        if (estimateUnpriced > 0)
        {
            warnings.Add($"{estimateUnpriced} materiali della distinta base senza prezzo a listino: la stima materiali è incompleta.");
        }

        decimal estimatedLabor = 0;
        foreach (var operation in order.Operations)
        {
            var rate = FindWorkCenter(operation.WorkCenter)?.HourlyRate;
            if (rate is { } r)
            {
                estimatedLabor += operation.EstimatedMinutes / 60 * r;
            }
            else
            {
                workCentersWithoutRate.Add(operation.WorkCenter ?? "(fase senza centro di lavoro)");
            }
        }

        if (longPhases > 0)
        {
            // Phase time is wall-clock (start to completion, or to now while open): a phase left open over
            // a night or a weekend counts every hour of it. Flag it instead of silently costing it.
            warnings.Add($"{longPhases} fasi durano più di un turno (8 ore tra avvio e fine, o sono aperte da allora): " +
                         "il tempo include probabilmente ore non lavorate. Verifica le fasi aperte o registra le ore effettive a mano.");
        }

        if (workCentersWithoutRate.Count > 0)
        {
            warnings.Add($"Senza tariffa oraria, quindi manodopera non valorizzata: {string.Join(", ", workCentersWithoutRate)}. Impostala in Centri di lavoro.");
        }

        if (order.SalePrice is null)
        {
            warnings.Add("Prezzo di vendita non impostato: il margine non si può calcolare.");
        }

        var actualMaterial = Round(materialLines.Sum(line => line.Cost));
        var actualLabor = Round(laborLines.Sum(line => line.Cost ?? 0));
        var estimated = new CostBreakdown(Round(estimatedMaterial), Round(estimatedLabor));
        var actual = new CostBreakdown(actualMaterial, actualLabor);

        return new WorkOrderCostingResult(
            order.Id, order.Code, order.Product.Code, order.Product.Name, order.Quantity, order.Status, order.SalePrice,
            estimated, actual,
            Margin(order.SalePrice, estimated.Total), Margin(order.SalePrice, actual.Total),
            Round(laborLines.Sum(line => line.Minutes)),
            materialLines.OrderBy(line => line.MaterialCode).ToList(), laborLines, warnings);
    }

    private static MarginResult? Margin(decimal? salePrice, decimal cost) =>
        salePrice is { } price
            ? new MarginResult(Round(price - cost), price > 0 ? Math.Round((price - cost) / price, 4) : null)
            : null;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>One 8-hour shift: a phase lasting longer than this is flagged in Warnings.</summary>
    public const decimal LongPhaseMinutes = 480;
}

public sealed record CostBreakdown(decimal Material, decimal Labor)
{
    public decimal Total => Material + Labor;
}

public sealed record MarginResult(decimal Amount, decimal? Ratio);

public sealed record MaterialCostLine(string MaterialCode, decimal Quantity, decimal Cost, string PriceSource, decimal UnpricedQuantity);

/// <summary>One labor line: a routing phase ("Fase") or a manual entry ("Ore registrate", deletable by
/// its <see cref="LaborEntryId"/>). Cost is null when the work center has no hourly rate.</summary>
public sealed record LaborCostLine(
    string Kind, string Description, string? WorkCenter, string? Operator, decimal Minutes,
    decimal? HourlyRate, decimal? Cost, bool InProgress, Guid? LaborEntryId);

public sealed record WorkOrderCostingResult(
    Guid WorkOrderId, string WorkOrderCode, string ProductCode, string ProductName, decimal Quantity, string Status,
    decimal? SalePrice, CostBreakdown Estimated, CostBreakdown Actual,
    MarginResult? EstimatedMargin, MarginResult? ActualMargin, decimal ActualMinutes,
    List<MaterialCostLine> Materials, List<LaborCostLine> Labor, List<string> Warnings);
