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

        var entries = new List<PlanningEntryResponse>();

        if (WantsType("WorkOrder"))
        {
            var workOrders = await _dbContext.WorkOrders
                .AsNoTracking()
                .Include(o => o.Product)
                .Where(o => o.DueDate != null && o.Status != "Completed" && o.Status != "Cancelled" &&
                    (siteId == null || (o.Area != null && o.Area.SiteId == siteId)))
                .ToListAsync(cancellationToken);

            entries.AddRange(workOrders.Select(o => new PlanningEntryResponse(
                "WorkOrder", o.Id, o.Code, o.Status, o.DueDate!.Value, o.Product.Name, "Consegna cliente")));
        }

        if (WantsType("PurchaseOrder"))
        {
            var purchaseOrders = await _dbContext.PurchaseOrders
                .AsNoTracking()
                .Include(order => order.Supplier)
                .Where(order => order.ExpectedDeliveryDate != null && order.Status != "Received" && order.Status != "Cancelled")
                .ToListAsync(cancellationToken);

            entries.AddRange(purchaseOrders.Select(order => new PlanningEntryResponse(
                "PurchaseOrder", order.Id, order.Code, order.Status, order.ExpectedDeliveryDate!.Value, order.Supplier.Name, "Consegna prevista")));
        }

        if (WantsType("Shipment"))
        {
            var shipments = await _dbContext.Shipments
                .AsNoTracking()
                .Include(s => s.Carrier)
                .Where(s => s.ExpectedAt != null && s.Status != "Delivered" && s.Status != "Cancelled")
                .ToListAsync(cancellationToken);

            entries.AddRange(shipments.Select(s => new PlanningEntryResponse(
                "Shipment", s.Id, s.Code, s.Status, s.ExpectedAt!.Value, s.Carrier.Name,
                s.Direction == "Inbound" ? "Spedizione in ingresso" : "Spedizione in uscita")));
        }

        if (WantsType("MaintenanceTask"))
        {
            var maintenanceTasks = await _dbContext.MaintenanceTasks
                .AsNoTracking()
                .Include(t => t.Equipment).ThenInclude(equipment => equipment!.WorkCenter)
                .Where(t => t.DueDate != null && t.Status == "Pending" &&
                    (siteId == null || (t.Equipment.WorkCenter != null && t.Equipment.WorkCenter.SiteId == siteId)))
                .ToListAsync(cancellationToken);

            entries.AddRange(maintenanceTasks.Select(t => new PlanningEntryResponse(
                "MaintenanceTask", t.Id, t.Title, t.Status, t.DueDate!.Value, t.Equipment.Name, "Manutenzione")));
        }

        var today = DateTime.UtcNow.Date;
        var thisWeekStart = StartOfWeek(today);
        var nextWeekStart = thisWeekStart.AddDays(7);
        var nextWeekEnd = nextWeekStart.AddDays(7);

        // Overdue/ThisWeek/NextWeek are deliberately global — real urgency regardless of which weeks are
        // currently being browsed, so switching the range never hides something overdue. Total is
        // deliberately scoped to the visible range instead ("Totale nel periodo" promises exactly that),
        // so it always matches the row count actually shown below it.
        var inRange = entries
            .Where(e => e.Date >= rangeStart && e.Date < rangeEnd)
            .OrderBy(e => e.Date)
            .ToList();

        var dashboard = new PlanningDashboardResponse(
            Overdue: entries.Count(e => e.Date.Date < today),
            ThisWeek: entries.Count(e => e.Date.Date >= thisWeekStart && e.Date.Date < nextWeekStart),
            NextWeek: entries.Count(e => e.Date.Date >= nextWeekStart && e.Date.Date < nextWeekEnd),
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
