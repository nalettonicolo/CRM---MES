using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Services;

/// <summary>Builds withdrawal slip items against the active material catalog and registers a
/// MissingMaterial for every line that can't be fully covered by stock. Shared by
/// WithdrawalSlipsController (manual pick lists) and WorkOrdersController (pick lists generated
/// from a product's bill of materials), so both stay behaviourally identical.</summary>
public sealed class WithdrawalItemBuilder
{
    private readonly ApplicationDbContext _dbContext;

    public WithdrawalItemBuilder(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<WithdrawalItem>> BuildAsync(
        Guid slipId,
        IReadOnlyList<CreateWithdrawalSlipItemRequest> requestItems,
        CancellationToken cancellationToken)
    {
        var requestedCodes = requestItems
            .Select(item => item.MaterialCode.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var catalogMaterials = await _dbContext.Materials
            .AsNoTracking()
            .Where(material => material.IsActive && requestedCodes.Contains(material.Code))
            .ToDictionaryAsync(material => material.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var items = new List<WithdrawalItem>();
        foreach (var requestItem in requestItems)
        {
            var code = requestItem.MaterialCode.Trim();
            var catalogMaterial = catalogMaterials.GetValueOrDefault(code);
            var item = new WithdrawalItem
            {
                MaterialCode = code,
                Description = catalogMaterial?.Name ?? requestItem.Description?.Trim() ?? "Materiale non catalogato",
                Quantity = requestItem.Quantity,
                Unit = catalogMaterial?.Unit ?? (string.IsNullOrWhiteSpace(requestItem.Unit) ? "pz" : requestItem.Unit.Trim()),
                IsMissing = catalogMaterial is null || catalogMaterial.Stock < requestItem.Quantity
            };
            // Explicitly marks the item Added. Without this, attaching a brand-new item to an
            // *already tracked* parent (the edit path) makes EF Core's change detection assume it
            // already exists in the database (its Guid Id looks like a real key, not a "new" default
            // value) and emit an UPDATE instead of an INSERT, which affects 0 rows and throws.
            _dbContext.WithdrawalItems.Add(item);
            items.Add(item);

            if (item.IsMissing)
            {
                _dbContext.MissingMaterials.Add(new MissingMaterial
                {
                    WithdrawalSlipId = slipId,
                    MaterialCode = item.MaterialCode,
                    Quantity = item.Quantity,
                    Source = catalogMaterials.ContainsKey(item.MaterialCode) ? "Stock" : "Catalog",
                    Status = "Open"
                });
            }
        }

        return items;
    }
}
