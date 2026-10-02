using CrmMes.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Api.Controllers;

/// <summary>A single read-only board aggregating every dated commitment already tracked elsewhere in
/// the app — commesse (customer delivery), ordini fornitore (expected delivery), spedizioni, interventi
/// di manutenzione — so they can be seen together on a week-by-week timeline instead of checked one
/// section at a time. Nothing new is stored here: every entry is derived live from its own module: this
/// controller only aggregates, colors (via Type, left to the client to map) and filters.</summary>
[ApiController]
[Route("api/planning")]
[Authorize]
public class PlanningController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public PlanningController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<PlanningResponse>> GetPlanning(
        [FromQuery] DateTime? from = null,
        [FromQuery] int weeks = 4,
        [FromQuery] Guid? siteId = null,
        [FromQuery] string? types = null,
        CancellationToken cancellationToken = default)
    {
        weeks = Math.Clamp(weeks, 1, 26);
        var rangeStart = DateTime.SpecifyKind((from ?? StartOfWeek(DateTime.UtcNow)).Date, DateTimeKind.Utc);
        var rangeEnd = rangeStart.AddDays(weeks * 7);

        var requestedTypes = string.IsNullOrWhiteSpace(types)
            ? null
            : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool WantsType(string type) => requestedTypes is null || requestedTypes.Contains(type);

        var today = DateTime.UtcNow.Date;
        var thisWeekStart = StartOfWeek(today);
        var nextWeekStart = thisWeekStart.AddDays(7);
        var nextWeekEnd = nextWeekStart.AddDays(7);
        var allDueDates = new List<DateTime>();
        var entries = new List<PlanningEntryResponse>();

        if (WantsType("WorkOrder"))
        {
            var open = _dbContext.WorkOrders.AsNoTracking()
                .Where(o => o.DueDate != null && o.Status != "Completed" && o.Status != "Cancelled" &&
                    (siteId == null || (o.Area != null && o.Area.SiteId == siteId)));
            allDueDates.AddRange(await open.Select(o => o.DueDate!.Value).ToListAsync(cancellationToken));
            entries.AddRange(await open
                .Where(o => o.DueDate >= rangeStart && o.DueDate < rangeEnd)
                .Select(o => new PlanningEntryResponse(
                    "WorkOrder", o.Id, o.Code, o.Status, o.DueDate!.Value, o.Product.Name, "Consegna cliente"))
                .ToListAsync(cancellationToken));
        }

        if (WantsType("PurchaseOrder"))
        {
            var open = _dbContext.PurchaseOrders.AsNoTracking()
                .Where(order => order.ExpectedDeliveryDate != null && order.Status != "Received" && order.Status != "Cancelled");
            allDueDates.AddRange(await open.Select(order => order.ExpectedDeliveryDate!.Value).ToListAsync(cancellationToken));
            entries.AddRange(await open
                .Where(order => order.ExpectedDeliveryDate >= rangeStart && order.ExpectedDeliveryDate < rangeEnd)
                .Select(order => new PlanningEntryResponse(
                    "PurchaseOrder", order.Id, order.Code, order.Status, order.ExpectedDeliveryDate!.Value, order.Supplier.Name, "Consegna prevista"))
                .ToListAsync(cancellationToken));
        }

        if (WantsType("Shipment"))
        {
            var open = _dbContext.Shipments.AsNoTracking()
                .Where(s => s.ExpectedAt != null && s.Status != "Delivered" && s.Status != "Cancelled");
            allDueDates.AddRange(await open.Select(s => s.ExpectedAt!.Value).ToListAsync(cancellationToken));
            entries.AddRange(await open
                .Where(s => s.ExpectedAt >= rangeStart && s.ExpectedAt < rangeEnd)
                .Select(s => new PlanningEntryResponse(
                    "Shipment", s.Id, s.Code, s.Status, s.ExpectedAt!.Value, s.Carrier.Name,
                    s.Direction == "Inbound" ? "Spedizione in ingresso" : "Spedizione in uscita"))
                .ToListAsync(cancellationToken));
        }

        if (WantsType("MaintenanceTask"))
        {
            var open = _dbContext.MaintenanceTasks.AsNoTracking()
                .Where(t => t.DueDate != null && t.Status == "Pending" &&
                    (siteId == null || (t.Equipment.WorkCenter != null && t.Equipment.WorkCenter.SiteId == siteId)));
            allDueDates.AddRange(await open.Select(t => t.DueDate!.Value).ToListAsync(cancellationToken));
            entries.AddRange(await open
                .Where(t => t.DueDate >= rangeStart && t.DueDate < rangeEnd)
                .Select(t => new PlanningEntryResponse(
                    "MaintenanceTask", t.Id, t.Title, t.Status, t.DueDate!.Value, t.Equipment.Name, "Manutenzione"))
                .ToListAsync(cancellationToken));
        }

        // Overdue/ThisWeek/NextWeek are deliberately global — real urgency regardless of which weeks are
        // currently being browsed. Total is scoped to the visible range.
        var inRange = entries.OrderBy(e => e.Date).ToList();
        var dashboard = new PlanningDashboardResponse(
            Overdue: allDueDates.Count(date => date.Date < today),
            ThisWeek: allDueDates.Count(date => date.Date >= thisWeekStart && date.Date < nextWeekStart),
            NextWeek: allDueDates.Count(date => date.Date >= nextWeekStart && date.Date < nextWeekEnd),
            Total: inRange.Count);

        return Ok(new PlanningResponse(rangeStart, weeks, inRange, dashboard));
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-diff);
    }
}

public sealed record PlanningEntryResponse(
    string Type, Guid Id, string Code, string Status, DateTime Date, string Detail, string Kind);

public sealed record PlanningDashboardResponse(int Overdue, int ThisWeek, int NextWeek, int Total);

public sealed record PlanningResponse(DateTime RangeStart, int Weeks, IReadOnlyList<PlanningEntryResponse> Entries, PlanningDashboardResponse Dashboard);
