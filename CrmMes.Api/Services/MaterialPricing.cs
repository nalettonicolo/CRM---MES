using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Services;

/// <summary>Catalog prices, looked up the same way everywhere a material has to be valued without an
/// actual purchase price: the quote's price estimate and the work order costing both use the cheapest
/// positive price known across suppliers.</summary>
public sealed class MaterialPricing
{
    private readonly ApplicationDbContext _dbContext;

    public MaterialPricing(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Cheapest catalog unit price per material code (case-insensitive keys). Codes with no
    /// priced supplier are simply absent, so callers can count them as missing instead of as zero.</summary>
    public async Task<Dictionary<string, decimal>> CheapestCatalogPricesAsync(
        IEnumerable<string> materialCodes, CancellationToken cancellationToken)
    {
        var codes = materialCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (codes.Count == 0)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        // Min computed in memory: Sqlite (test provider) can't aggregate decimals server-side.
        var entries = await _dbContext.MaterialSuppliers.AsNoTracking()
            .Where(link => codes.Contains(link.Material.Code) && link.UnitPrice > 0)
            .Select(link => new { link.Material.Code, link.UnitPrice })
            .ToListAsync(cancellationToken);

        return entries
            .GroupBy(entry => entry.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(entry => entry.UnitPrice), StringComparer.OrdinalIgnoreCase);
    }
}
