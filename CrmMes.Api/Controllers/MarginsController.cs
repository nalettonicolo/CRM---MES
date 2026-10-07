using CrmMes.Api.Services;
using CrmMes.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>Controllo margini: per le commesse più recenti, prezzo di vendita, costo stimato e reale e
/// margine, con i totali del periodo. Riservato alla direzione (policy ViewMargins).</summary>
[ApiController]
[Authorize(Policy = "ViewMargins")]
[Route("api/margins")]
public class MarginsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly WorkOrderCosting _costing;

    public MarginsController(ApplicationDbContext dbContext, WorkOrderCosting costing)
    {
        _dbContext = dbContext;
        _costing = costing;
    }

    /// <param name="take">Most recent work orders to include (default 50, max 200), costed together in a
    /// fixed number of queries (see WorkOrderCosting.CalculateManyAsync).</param>
    [HttpGet]
    public async Task<ActionResult<MarginOverviewResponse>> GetMargins(
        [FromQuery] string? status = null,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var query = _dbContext.WorkOrders.AsNoTracking().Where(order => order.Status != "Cancelled");
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(order => order.Status == status.Trim());
        }

        var orders = await query
            .OrderByDescending(order => order.CreatedAt)
            .Take(take)
            .Select(order => new { order.Id, CustomerName = order.Customer != null ? order.Customer.Name : order.CustomerReference })
            .ToListAsync(cancellationToken);

        var customers = orders.ToDictionary(order => order.Id, order => order.CustomerName);
        var costings = await _costing.CalculateManyAsync(orders.Select(order => order.Id).ToList(), DateTime.UtcNow, cancellationToken);
        var rows = costings
            .Select(costing => new MarginRowResponse(
                costing.WorkOrderId, costing.WorkOrderCode, costing.ProductName, customers[costing.WorkOrderId], costing.Status,
                costing.SalePrice, costing.Estimated.Total, costing.Actual.Total,
                costing.EstimatedMargin?.Ratio, costing.ActualMargin?.Amount, costing.ActualMargin?.Ratio,
                costing.Warnings.Count))
            .ToList();

        var priced = rows.Where(row => row.SalePrice.HasValue).ToList();
        var revenue = priced.Sum(row => row.SalePrice!.Value);
        var cost = priced.Sum(row => row.ActualCost);
        AuditTrail.Add(_dbContext, User, "MarginsViewed", "Margins", null, $"Controllo margini consultato: {rows.Count} commesse.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new MarginOverviewResponse(
            rows.Count, priced.Count, revenue, cost, revenue - cost,
            revenue > 0 ? Math.Round((revenue - cost) / revenue, 4) : null,
            rows.Count(row => row.ActualMargin is < 0),
            rows));
    }
}

public sealed record MarginRowResponse(
    Guid WorkOrderId, string WorkOrderCode, string ProductName, string? CustomerName, string Status,
    decimal? SalePrice, decimal EstimatedCost, decimal ActualCost,
    decimal? EstimatedMarginRatio, decimal? ActualMargin, decimal? ActualMarginRatio, int WarningCount);

/// <summary>Totals cover only work orders with a sale price: a job with no price has no margin, and
/// counting its cost would make the period look worse than it is.</summary>
public sealed record MarginOverviewResponse(
    int WorkOrderCount, int PricedWorkOrderCount, decimal Revenue, decimal ActualCost, decimal Margin,
    decimal? MarginRatio, int LossMakingCount, List<MarginRowResponse> WorkOrders);
