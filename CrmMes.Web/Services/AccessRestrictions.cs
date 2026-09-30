namespace CrmMes.Web.Services;

public static class AccessRestrictions
{
    /// <summary>Only what differs from "every channel" is stored: a role or area with everything ticked is
    /// left out, so a channel switched on later applies to it automatically.</summary>
    public static Dictionary<string, List<string>> From(Dictionary<string, HashSet<string>> ticks, IReadOnlyList<string> channels) =>
        ticks
            .Where(pair => channels.Any(channel => !pair.Value.Contains(channel)))
            .ToDictionary(pair => pair.Key, pair => channels.Where(pair.Value.Contains).ToList());
}
