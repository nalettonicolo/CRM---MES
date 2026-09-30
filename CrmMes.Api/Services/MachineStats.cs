using CrmMes.Core.Models;

namespace CrmMes.Api.Services;

/// <summary>Turns a machine's readings into the figures of a period: minutes per state, pieces made,
/// availability. A state lasts until the next reading; a silence longer than <see cref="MaxGap"/> counts
/// as "no data" (network down, gateway off) instead of stretching the last state over it, so a machine
/// that stops reporting never looks like it ran all night.</summary>
public static class MachineStats
{
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(15);
    public static readonly string[] States = ["Running", "Idle", "Setup", "Stopped", "Alarm", "Off"];

    public sealed record Result(
        IReadOnlyDictionary<string, decimal> MinutesByState, decimal NoDataMinutes, long Pieces, long Scrap,
        decimal? Availability, int AlarmCount);

    /// <param name="events">Readings of the machine, any order; the last one before <paramref name="from"/>
    /// (if given) sets the state at the start of the period.</param>
    public static Result Compute(IReadOnlyList<MachineEvent> events, DateTime from, DateTime to)
    {
        var minutes = States.ToDictionary(s => s, _ => 0m);
        decimal noData = 0;
        var ordered = events.OrderBy(e => e.Timestamp).ToList();
        var cursor = from;
        MachineEvent? current = ordered.LastOrDefault(e => e.Timestamp <= from);
        foreach (var next in ordered.Where(e => e.Timestamp > from && e.Timestamp < to).Cast<MachineEvent?>().Append(null))
        {
            var until = next?.Timestamp ?? to;
            if (until > cursor)
            {
                var span = until - cursor;
                if (current is null || cursor - current.Timestamp > MaxGap)
                {
                    noData += (decimal)span.TotalMinutes;
                }
                else
                {
                    // Covered by the reading up to MaxGap after it; beyond that the machine went silent.
                    var coveredUntil = Min(until, current.Timestamp + MaxGap);
                    var covered = coveredUntil > cursor ? (decimal)(coveredUntil - cursor).TotalMinutes : 0;
                    minutes[current.State] = minutes.GetValueOrDefault(current.State) + covered;
                    noData += (decimal)span.TotalMinutes - covered;
                }
            }

            if (next is not null)
            {
                current = next;
                cursor = next.Timestamp;
            }
        }

        var inPeriod = ordered.Where(e => e.Timestamp >= from && e.Timestamp < to).ToList();
        var startReading = ordered.LastOrDefault(e => e.Timestamp < from);
        var pieces = CounterDelta(startReading?.PieceCounter, inPeriod.Select(e => e.PieceCounter));
        var scrap = CounterDelta(startReading?.ScrapCounter, inPeriod.Select(e => e.ScrapCounter));

        // Availability: running time over the time the machine was meant to work (everything but Off
        // and unknown time), the "Disponibilità" factor of OEE.
        var planned = minutes.Where(m => m.Key != "Off").Sum(m => m.Value);
        decimal? availability = planned > 0 ? Math.Round(minutes["Running"] / planned, 4) : null;
        var alarms = inPeriod.Count(e => e.State == "Alarm" && (ordered.IndexOf(e) == 0 || ordered[ordered.IndexOf(e) - 1].State != "Alarm"));

        return new Result(minutes.ToDictionary(m => m.Key, m => Math.Round(m.Value, 1)), Math.Round(noData, 1), pieces, scrap, availability, alarms);
    }

    /// <summary>Pieces from a cumulative counter: the sum of its increases. A drop means the counter was
    /// reset (new shift, power cycle): the new value counts from zero.</summary>
    public static long CounterDelta(long? start, IEnumerable<long?> values)
    {
        long total = 0;
        var previous = start;
        foreach (var value in values.Where(v => v.HasValue).Select(v => v!.Value))
        {
            if (previous is { } last)
            {
                total += value >= last ? value - last : value;
            }

            previous = value;
        }

        return total;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
