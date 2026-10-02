namespace CrmMes.Api.Services;

/// <summary>Day-granularity finite-capacity helpers shared by work-order scheduling and capacity-plan views.
/// Load from an operation is spread evenly across its planned start/end calendar days.</summary>
public static class FiniteCapacityScheduler
{
    public static Dictionary<string, Dictionary<DateOnly, decimal>> NewCommittedLedger()
        => new(StringComparer.OrdinalIgnoreCase);

    public static void AddSpreadLoad(
        Dictionary<string, Dictionary<DateOnly, decimal>> committed,
        string workCenter,
        DateOnly startDay,
        DateOnly endDay,
        decimal estimatedMinutes)
    {
        var spanDays = endDay.DayNumber - startDay.DayNumber + 1;
        var perDay = estimatedMinutes / spanDays;

        if (!committed.TryGetValue(workCenter, out var byDay))
        {
            byDay = new Dictionary<DateOnly, decimal>();
            committed[workCenter] = byDay;
        }

        for (var day = startDay; day <= endDay; day = day.AddDays(1))
        {
            byDay[day] = byDay.GetValueOrDefault(day) + perDay;
        }
    }

    public static void AddSpreadLoad(
        Dictionary<string, Dictionary<DateOnly, decimal>> committed,
        string workCenter,
        DateTime plannedStartAt,
        DateTime plannedEndAt,
        decimal estimatedMinutes)
    {
        AddSpreadLoad(
            committed,
            workCenter,
            DateOnly.FromDateTime(plannedStartAt),
            DateOnly.FromDateTime(plannedEndAt),
            estimatedMinutes);
    }

    /// <summary>Places one operation forward from <paramref name="earliestDay"/> and updates the committed ledger.</summary>
    public static (DateOnly StartDay, DateOnly EndDay) PlaceOperation(
        decimal estimatedMinutes,
        string? matchedWorkCenterName,
        decimal dailyCapacityMinutes,
        DateOnly earliestDay,
        Dictionary<string, Dictionary<DateOnly, decimal>> committed)
    {
        if (matchedWorkCenterName is null || dailyCapacityMinutes <= 0)
        {
            return (earliestDay, earliestDay);
        }

        if (!committed.TryGetValue(matchedWorkCenterName, out var byDay))
        {
            byDay = new Dictionary<DateOnly, decimal>();
            committed[matchedWorkCenterName] = byDay;
        }

        var remaining = estimatedMinutes;
        var day = earliestDay;
        DateOnly? firstDay = null;
        var lastDay = earliestDay;
        while (remaining > 0)
        {
            var used = byDay.GetValueOrDefault(day);
            var available = dailyCapacityMinutes - used;
            if (available > 0)
            {
                firstDay ??= day;
                var consumed = Math.Min(available, remaining);
                byDay[day] = used + consumed;
                remaining -= consumed;
                lastDay = day;
            }

            day = day.AddDays(1);
        }

        return (firstDay ?? earliestDay, lastDay);
    }

    public static IReadOnlyList<WorkCenterCapacityPlanRow> BuildCapacityPlan(
        IEnumerable<(string Code, string Name, decimal DailyCapacityMinutes)> workCenters,
        IEnumerable<(string WorkCenter, DateTime PlannedStartAt, DateTime PlannedEndAt, decimal EstimatedMinutes)> scheduledOpen,
        DateOnly from,
        int days)
    {
        var committed = NewCommittedLedger();
        foreach (var op in scheduledOpen)
        {
            if (string.IsNullOrWhiteSpace(op.WorkCenter))
            {
                continue;
            }

            AddSpreadLoad(committed, op.WorkCenter, op.PlannedStartAt, op.PlannedEndAt, op.EstimatedMinutes);
        }

        var results = new List<WorkCenterCapacityPlanRow>();
        foreach (var workCenter in workCenters)
        {
            committed.TryGetValue(workCenter.Name, out var byDay);

            for (var offset = 0; offset < days; offset++)
            {
                var day = from.AddDays(offset);
                var capacity = workCenter.DailyCapacityMinutes;
                var committedMinutes = byDay?.GetValueOrDefault(day) ?? 0;
                var freeMinutes = Math.Max(0, capacity - committedMinutes);
                results.Add(new WorkCenterCapacityPlanRow(
                    workCenter.Code,
                    workCenter.Name,
                    day,
                    capacity,
                    committedMinutes,
                    freeMinutes));
            }
        }

        return results;
    }
}

public sealed record WorkCenterCapacityPlanRow(
    string WorkCenterCode,
    string WorkCenterName,
    DateOnly Day,
    decimal CapacityMinutes,
    decimal CommittedMinutes,
    decimal FreeMinutes);
