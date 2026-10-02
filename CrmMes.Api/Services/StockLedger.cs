using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Services;

/// <summary>Single place that moves <see cref="Material.Stock"/> and the lot ledger. Withdrawal close,
/// DDT issue (catalog material lines) and signed site reports all go through here so a quantity leaving
/// the company is not recorded twice in different modules and then forgotten in the warehouse.</summary>
public sealed class StockLedger
{
    private readonly ApplicationDbContext _dbContext;

    public StockLedger(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Dictionary<string, Material>> LoadActiveAsync(
        IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var list = codes.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (list.Length == 0)
        {
            return new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        }

        return await _dbContext.Materials
            .Where(material => list.Contains(material.Code) && material.IsActive)
            .ToDictionaryAsync(material => material.Code, cancellationToken);
    }

    public static string[] Unavailable(
        IReadOnlyDictionary<string, Material> materials, IEnumerable<(string Code, decimal Quantity)> needed) =>
        needed
            .Where(line => !materials.TryGetValue(line.Code, out var material) || material.Stock < line.Quantity)
            .Select(line => line.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task<Dictionary<string, List<MaterialLot>>> LoadOpenLotsAsync(
        IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var list = codes.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct().ToArray();
        var openLots = list.Length == 0
            ? []
            : await _dbContext.MaterialLots
                .Where(lot => list.Contains(lot.MaterialCode) && lot.Quantity > 0)
                .ToListAsync(cancellationToken);
        return openLots
            .GroupBy(lot => lot.MaterialCode)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(lot => lot.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(lot => lot.ReceivedAt)
                .ToList());
    }

    /// <summary>Decrements stock and consumes lots FEFO/FIFO. Genealogy rows are written only when a
    /// withdrawal item is supplied — DDT and rapportini still move stock, without pretending they were a
    /// pick list. Remainder not covered by lots is still taken off <see cref="Material.Stock"/>.</summary>
    public List<(MaterialLot Lot, decimal Quantity)> Consume(
        Material material,
        decimal quantity,
        IReadOnlyDictionary<string, List<MaterialLot>> lotsByMaterial,
        Guid? withdrawalItemId)
    {
        material.Stock -= quantity;
        var remaining = quantity;
        var consumedLots = new List<(MaterialLot Lot, decimal Quantity)>();
        if (!lotsByMaterial.TryGetValue(material.Code, out var lots))
        {
            return consumedLots;
        }

        foreach (var lot in lots)
        {
            if (remaining <= 0)
            {
                break;
            }

            var consumed = Math.Min(remaining, lot.Quantity);
            if (consumed <= 0)
            {
                continue;
            }

            lot.Quantity -= consumed;
            remaining -= consumed;
            consumedLots.Add((lot, consumed));
            if (withdrawalItemId.HasValue)
            {
                _dbContext.MaterialLotConsumptions.Add(new MaterialLotConsumption
                {
                    MaterialLotId = lot.Id,
                    WithdrawalItemId = withdrawalItemId.Value,
                    Quantity = consumed
                });
            }
        }

        return consumedLots;
    }

    /// <summary>Takes what is in stock, never more: used when a signed rapportino lists extra van
    /// materials that were not on a closed pick list. Hours still book even if the warehouse is empty.</summary>
    public async Task ConsumeAvailableAsync(
        IEnumerable<(string Code, decimal Quantity)> lines, CancellationToken cancellationToken)
    {
        var needed = lines.Where(line => line.Quantity > 0).ToList();
        if (needed.Count == 0)
        {
            return;
        }

        var materials = await LoadActiveAsync(needed.Select(line => line.Code), cancellationToken);
        var lots = await LoadOpenLotsAsync(materials.Keys, cancellationToken);
        foreach (var (code, quantity) in needed)
        {
            if (!materials.TryGetValue(code, out var material))
            {
                continue;
            }

            var take = Math.Min(material.Stock, quantity);
            if (take > 0)
            {
                Consume(material, take, lots, withdrawalItemId: null);
            }
        }

        await RaiseMinStockAlertsAsync(materials.Values, cancellationToken);
    }

    public async Task RestoreAsync(
        IEnumerable<(string Code, decimal Quantity)> lines, string lotPrefix, CancellationToken cancellationToken)
    {
        var materials = await LoadActiveAsync(lines.Select(line => line.Code), cancellationToken);
        foreach (var (code, quantity) in lines.Where(line => line.Quantity > 0))
        {
            if (!materials.TryGetValue(code, out var material))
            {
                continue;
            }

            material.Stock += quantity;
            _dbContext.MaterialLots.Add(new MaterialLot
            {
                MaterialCode = code,
                LotNumber = $"{lotPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
                Quantity = quantity,
                InitialQuantity = quantity,
                Notes = "Rientro da documento di trasporto annullato"
            });
        }
    }

    public async Task RaiseMinStockAlertsAsync(IEnumerable<Material> materials, CancellationToken cancellationToken)
    {
        var belowMinimum = materials
            .Where(material => material.MinStock > 0 && material.Stock < material.MinStock)
            .ToList();
        if (belowMinimum.Count == 0)
        {
            return;
        }

        var codes = belowMinimum.Select(material => material.Code).ToArray();
        var alreadyOpen = await _dbContext.MissingMaterials
            .Where(mm => mm.Status == "Open" && mm.Source == "MinStock" && codes.Contains(mm.MaterialCode))
            .Select(mm => mm.MaterialCode)
            .ToListAsync(cancellationToken);
        var alreadyOpenSet = alreadyOpen.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var material in belowMinimum.Where(material => !alreadyOpenSet.Contains(material.Code)))
        {
            _dbContext.MissingMaterials.Add(new MissingMaterial
            {
                MaterialCode = material.Code,
                Quantity = material.MinStock - material.Stock,
                Source = "MinStock",
                Status = "Open"
            });
        }
    }
}
