using System.Text.Json;

namespace CrmMes.Api.Services;

/// <summary>From where people can use the system, as decided by the Admin: the Windows program
/// ("desktop"), the web platform ("web") and the technicians' phone page ("mobile"). Three levels:
/// the channels the company uses at all; for each role, which of those it may log in from; for each
/// area (the always-on ones and the optional modules), on which of desktop and web it is shown.
/// Nothing configured means everything everywhere, exactly as before this existed.
///
/// The Admin can always log in from every channel the company uses and always sees the settings, so a
/// wrong configuration can never lock the company out. The channel is a company policy applied at login
/// and in the menus, not a security boundary: permissions stay those of the role, on every channel.</summary>
public static class AccessChannels
{
    public const string Desktop = "desktop";
    public const string Web = "web";
    public const string Mobile = "mobile";

    public static readonly IReadOnlyList<string> All = [Desktop, Web, Mobile];

    /// <summary>Channels an area can be shown on (the phone page has its own fixed functions).</summary>
    public static readonly IReadOnlyList<string> AreaChannels = [Desktop, Web];

    public static readonly IReadOnlyList<string> Roles = ["Admin", "Management", "Sales", "Purchasing", "Warehouse", "Operator"];

    public sealed record AreaInfo(string Key, string Name, string? Module);

    /// <summary>Always-on areas first, then one per optional module (shown only if the module is on).</summary>
    public static IReadOnlyList<AreaInfo> Areas { get; } =
    [
        new("dashboard", "Cruscotto", null),
        new("production", "Produzione e commesse", null),
        new("warehouse", "Magazzino e lotti", null),
        new("registry", "Anagrafiche (aree, sedi, centri, fornitori)", null),
        new("users", "Utenti", null),
        .. Sectors.Modules.Where(module => module.Available).Select(module => new AreaInfo(module.Key, module.Name, module.Key)),
    ];

    public sealed class Settings
    {
        public List<string> Channels { get; set; } = [.. All];
        public Dictionary<string, List<string>> Roles { get; set; } = [];
        public Dictionary<string, List<string>> Areas { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Settings Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return new Settings();
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<Settings>(stored, Json) ?? new Settings());
        }
        catch (JsonException)
        {
            return new Settings();
        }
    }

    public static string Serialize(Settings settings) => JsonSerializer.Serialize(Normalize(settings), Json);

    /// <summary>A message for the Admin, or null when the settings can be saved.</summary>
    public static string? Validate(Settings settings)
    {
        var unknownChannel = settings.Channels
            .Concat(settings.Roles.Values.SelectMany(list => list))
            .FirstOrDefault(channel => !All.Contains(channel));
        if (unknownChannel is not null)
        {
            return $"Canale non riconosciuto: {unknownChannel}.";
        }

        if (!settings.Channels.Contains(Desktop) && !settings.Channels.Contains(Web))
        {
            return "Serve almeno il programma desktop o la piattaforma web: da uno dei due si configura il gestionale.";
        }

        var unknownRole = settings.Roles.Keys.FirstOrDefault(role => !Roles.Contains(role));
        if (unknownRole is not null)
        {
            return $"Ruolo non riconosciuto: {unknownRole}.";
        }

        var unknownArea = settings.Areas.Keys.FirstOrDefault(area => Areas.All(known => known.Key != area));
        if (unknownArea is not null)
        {
            return $"Area non riconosciuta: {unknownArea}.";
        }

        var badAreaChannel = settings.Areas.Values.SelectMany(list => list).FirstOrDefault(channel => !AreaChannels.Contains(channel));
        return badAreaChannel is null ? null : $"Un'area può essere mostrata solo su desktop o web, non su {badAreaChannel}.";
    }

    public static bool CanLogIn(Settings settings, string role, string channel)
    {
        if (!settings.Channels.Contains(channel))
        {
            return false;
        }

        if (role == "Admin")
        {
            return true;
        }

        return !settings.Roles.TryGetValue(role, out var allowed) || allowed.Contains(channel);
    }

    /// <summary>Areas shown on a channel, given the modules the company has switched on. "settings" is
    /// not an area: the Admin always has it.</summary>
    public static List<string> AreasFor(Settings settings, IReadOnlyCollection<string> enabledModules, string channel) =>
        Areas
            .Where(area => area.Module is null || enabledModules.Contains(area.Module))
            .Where(area => !settings.Areas.TryGetValue(area.Key, out var channels) || channels.Contains(channel))
            .Select(area => area.Key)
            .ToList();

    /// <summary>Login requests from clients older than this feature carry no channel: they are the Windows
    /// program. Anything unknown is refused rather than guessed.</summary>
    public static string? NormalizeChannel(string? channel) =>
        string.IsNullOrWhiteSpace(channel) ? Desktop
        : All.FirstOrDefault(known => string.Equals(known, channel.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string ChannelName(string channel) => channel switch
    {
        Desktop => "programma desktop",
        Web => "piattaforma web",
        Mobile => "pagina web dei tecnici",
        _ => channel,
    };

    private static Settings Normalize(Settings settings) => new()
    {
        Channels = settings.Channels.Select(c => c.Trim().ToLowerInvariant()).Distinct().OrderBy(All.IndexOf).ToList(),
        Roles = settings.Roles.ToDictionary(
            pair => pair.Key.Trim(),
            pair => pair.Value.Select(c => c.Trim().ToLowerInvariant()).Distinct().OrderBy(All.IndexOf).ToList()),
        Areas = settings.Areas.ToDictionary(
            pair => pair.Key.Trim(),
            pair => pair.Value.Select(c => c.Trim().ToLowerInvariant()).Distinct().OrderBy(All.IndexOf).ToList()),
    };

    private static int IndexOf(this IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
