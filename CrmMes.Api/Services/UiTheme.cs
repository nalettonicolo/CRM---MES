using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrmMes.Api.Services;

/// <summary>Company-wide visual appearance: colours, corner radius, field density and page background.
/// Stored as JSON on <c>CompanyProfile.UiTheme</c>. Empty means the built-in "Officina" defaults
/// (same look as before this existed). Applied by the web (CSS variables) and the desktop client
/// (runtime brushes).</summary>
public static partial class UiTheme
{
    public const string PresetOfficina = "officina";
    public const string PresetCarbone = "carbone";
    public const string PresetCarta = "carta";
    public const string PresetBlu = "blu-tecnico";
    public const string PresetVerde = "verde-officina";

    public static readonly IReadOnlyList<string> PresetKeys =
        [PresetOfficina, PresetCarbone, PresetCarta, PresetBlu, PresetVerde];

    public sealed class Settings
    {
        /// <summary>Named starting point; individual colours may override it.</summary>
        public string Preset { get; set; } = PresetOfficina;

        public string Background { get; set; } = "#E6E6E1";
        public string Surface { get; set; } = "#F5F5F1";
        public string SurfaceRaised { get; set; } = "#ECECE7";
        public string Ink { get; set; } = "#141414";
        public string Muted { get; set; } = "#555550";
        public string Line { get; set; } = "#C2C2BA";
        public string Accent { get; set; } = "#CF2A1F";
        public string AccentHover { get; set; } = "#B0241A";
        public string AccentSoft { get; set; } = "#F5E4E2";
        public string OnAccent { get; set; } = "#FFF8F7";
        public string Sidebar { get; set; } = "#161616";
        public string SidebarText { get; set; } = "#A3A39C";
        public string Ok { get; set; } = "#2B5A36";
        public string Warn { get; set; } = "#8F5A10";

        /// <summary>0 = sharp (Officina), up to 16 = soft cards.</summary>
        public int Radius { get; set; }

        /// <summary>"compact" | "comfortable" | "airy".</summary>
        public string Density { get; set; } = "comfortable";

        /// <summary>"solid" | "grid" | "grain".</summary>
        public string BackgroundStyle { get; set; } = "solid";

        /// <summary>Input/control border thickness in px (1–2).</summary>
        public int FieldBorder { get; set; } = 1;

        /// <summary>Minimum control height in px (30–48).</summary>
        public int FieldHeight { get; set; } = 38;

        /// <summary>Chiave di <see cref="Fonts"/>: non un nome libero, per evitare un valore che non esiste
        /// su Windows (il desktop lo traduce in un <c>FontFamily</c> reale) o uno scorretto nel CSS.</summary>
        public string FontFamily { get; set; } = FontDefault;
    }

    public sealed record PresetInfo(string Key, string Name, string Description, Settings Values);

    public const string FontDefault = "default";
    public const string FontSegoe = "segoe";
    public const string FontCalibri = "calibri";
    public const string FontGeorgia = "georgia";
    public const string FontVerdana = "verdana";

    /// <summary>Un font selezionabile dall'Admin: <see cref="CssStack"/> per il web (con alternative se il
    /// carattere non è installato), <see cref="WpfFamily"/> per il programma desktop (un solo nome: WPF
    /// usa i font di sistema, non ne scarica).</summary>
    public sealed record FontOption(string Key, string Label, string CssStack, string WpfFamily);

    public static readonly IReadOnlyList<FontOption> Fonts =
    [
        new(FontDefault, "Predefinito (IBM Plex Sans)", "\"IBM Plex Sans\", \"Bahnschrift\", \"Segoe UI\", system-ui, sans-serif", "Segoe UI"),
        new(FontSegoe, "Segoe UI", "\"Segoe UI\", system-ui, sans-serif", "Segoe UI"),
        new(FontCalibri, "Calibri", "Calibri, \"Segoe UI\", sans-serif", "Calibri"),
        new(FontGeorgia, "Georgia (con grazie)", "Georgia, \"Times New Roman\", serif", "Georgia"),
        new(FontVerdana, "Verdana", "Verdana, \"Segoe UI\", sans-serif", "Verdana"),
    ];

    public static FontOption FontFor(string? key) =>
        Fonts.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Fonts[0];

    public static IReadOnlyList<PresetInfo> Presets { get; } =
    [
        new(PresetOfficina, "Officina", "Carta neutra, rosso segnale, angoli a squadra (predefinito).", Default()),
        new(PresetCarbone, "Carbone", "Sfondo scuro, accento rosso chiaro, per ambienti poco illuminati.",
            new Settings
            {
                Preset = PresetCarbone,
                Background = "#141414", Surface = "#1C1C1C", SurfaceRaised = "#242424",
                Ink = "#F5F5F1", Muted = "#A3A39C", Line = "#2A2A2A",
                Accent = "#E85A4F", AccentHover = "#F0786E", AccentSoft = "#2A1816", OnAccent = "#141414",
                Sidebar = "#0E0E0E", SidebarText = "#A3A39C", Ok = "#7CB88A", Warn = "#D4A05A",
                Radius = 0, Density = "comfortable", BackgroundStyle = "solid", FieldBorder = 1, FieldHeight = 38,
            }),
        new(PresetCarta, "Carta chiara", "Bianco documentale, angoli morbidi, densità ariosa.",
            new Settings
            {
                Preset = PresetCarta,
                Background = "#F7F7F4", Surface = "#FFFFFF", SurfaceRaised = "#F0F0EC",
                Ink = "#1A1A1A", Muted = "#5C5C58", Line = "#D4D4CE",
                Accent = "#B91C14", AccentHover = "#9A1711", AccentSoft = "#F8E9E7", OnAccent = "#FFFFFF",
                Sidebar = "#1A1A1A", SidebarText = "#B8B8B2", Ok = "#2B5A36", Warn = "#8F5A10",
                Radius = 8, Density = "airy", BackgroundStyle = "solid", FieldBorder = 1, FieldHeight = 40,
            }),
        new(PresetBlu, "Blu tecnico", "Accento blu industriale, griglia sottile sullo sfondo.",
            new Settings
            {
                Preset = PresetBlu,
                Background = "#E4E8EC", Surface = "#F3F5F7", SurfaceRaised = "#E8ECF0",
                Ink = "#12181E", Muted = "#4A5560", Line = "#B8C0C8",
                Accent = "#1F4E79", AccentHover = "#173A5C", AccentSoft = "#DCE8F2", OnAccent = "#F5F9FC",
                Sidebar = "#14202C", SidebarText = "#9AA8B5", Ok = "#2B5A36", Warn = "#8F5A10",
                Radius = 2, Density = "comfortable", BackgroundStyle = "grid", FieldBorder = 1, FieldHeight = 38,
            }),
        new(PresetVerde, "Verde officina", "Accento verde meccanico, campi più alti.",
            new Settings
            {
                Preset = PresetVerde,
                Background = "#E4E8E4", Surface = "#F3F5F3", SurfaceRaised = "#E8ECE8",
                Ink = "#141814", Muted = "#4E554E", Line = "#B8C0B8",
                Accent = "#2F6B4F", AccentHover = "#24553E", AccentSoft = "#DCEBE3", OnAccent = "#F5FBF7",
                Sidebar = "#142018", SidebarText = "#9AA89A", Ok = "#2B5A36", Warn = "#8F5A10",
                Radius = 4, Density = "comfortable", BackgroundStyle = "grain", FieldBorder = 2, FieldHeight = 42,
            }),
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Settings Default() => new();

    public static Settings Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return Default();
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<Settings>(stored, Json) ?? Default());
        }
        catch (JsonException)
        {
            return Default();
        }
    }

    public static string Serialize(Settings settings) => JsonSerializer.Serialize(Normalize(settings), Json);

    public static Settings FromPreset(string? key)
    {
        var preset = Presets.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        return Normalize(Clone(preset?.Values ?? Default()));
    }

    public static Settings Normalize(Settings s)
    {
        s.Preset = PresetKeys.Contains(s.Preset, StringComparer.OrdinalIgnoreCase) ? s.Preset.ToLowerInvariant() : PresetOfficina;
        s.Background = Hex(s.Background, "#E6E6E1");
        s.Surface = Hex(s.Surface, "#F5F5F1");
        s.SurfaceRaised = Hex(s.SurfaceRaised, "#ECECE7");
        s.Ink = Hex(s.Ink, "#141414");
        s.Muted = Hex(s.Muted, "#555550");
        s.Line = Hex(s.Line, "#C2C2BA");
        s.Accent = Hex(s.Accent, "#CF2A1F");
        s.AccentHover = Hex(s.AccentHover, "#B0241A");
        s.AccentSoft = Hex(s.AccentSoft, "#F5E4E2");
        s.OnAccent = Hex(s.OnAccent, "#FFF8F7");
        s.Sidebar = Hex(s.Sidebar, "#161616");
        s.SidebarText = Hex(s.SidebarText, "#A3A39C");
        s.Ok = Hex(s.Ok, "#2B5A36");
        s.Warn = Hex(s.Warn, "#8F5A10");
        s.Radius = Math.Clamp(s.Radius, 0, 16);
        s.Density = s.Density is "compact" or "comfortable" or "airy" ? s.Density : "comfortable";
        s.BackgroundStyle = s.BackgroundStyle is "solid" or "grid" or "grain" ? s.BackgroundStyle : "solid";
        s.FieldBorder = Math.Clamp(s.FieldBorder, 1, 2);
        s.FieldHeight = Math.Clamp(s.FieldHeight, 30, 48);
        s.FontFamily = Fonts.Any(f => string.Equals(f.Key, s.FontFamily, StringComparison.OrdinalIgnoreCase)) ? s.FontFamily.ToLowerInvariant() : FontDefault;
        return s;
    }

    public static string? Validate(Settings settings)
    {
        Normalize(settings);
        return null;
    }

    /// <summary>CSS custom properties for the web platform (and printable docs).</summary>
    public static Dictionary<string, string> ToCssVariables(Settings s)
    {
        s = Normalize(s);
        var densityPad = s.Density switch { "compact" => "10px", "airy" => "22px", _ => "16px" };
        var densityGap = s.Density switch { "compact" => "8px", "airy" => "18px", _ => "12px" };
        return new Dictionary<string, string>
        {
            ["--bg"] = s.Background,
            ["--surface"] = s.Surface,
            ["--surface-2"] = s.SurfaceRaised,
            ["--ink"] = s.Ink,
            ["--muted"] = s.Muted,
            ["--faint"] = s.Muted,
            ["--line"] = s.Line,
            ["--line-strong"] = s.Line,
            ["--accent"] = s.Accent,
            ["--accent-hover"] = s.AccentHover,
            ["--accent-soft"] = s.AccentSoft,
            ["--on-accent"] = s.OnAccent,
            ["--ok"] = s.Ok,
            ["--warn"] = s.Warn,
            ["--danger"] = s.Accent,
            ["--danger-bg"] = s.AccentSoft,
            ["--radius"] = $"{s.Radius}px",
            ["--radius-sm"] = $"{Math.Max(0, s.Radius / 2)}px",
            ["--field-border"] = $"{s.FieldBorder}px",
            ["--field-height"] = $"{s.FieldHeight}px",
            ["--density-pad"] = densityPad,
            ["--density-gap"] = densityGap,
            ["--bg-style"] = s.BackgroundStyle,
            ["--sidebar-bg"] = s.Sidebar,
            ["--sidebar-text"] = s.SidebarText,
            ["--font"] = FontFor(s.FontFamily).CssStack,
        };
    }

    private static Settings Clone(Settings s) => Parse(Serialize(s));

    private static string Hex(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var v = value.Trim();
        if (!v.StartsWith('#'))
        {
            v = "#" + v;
        }

        return HexRegex().IsMatch(v) ? v.ToUpperInvariant() : fallback;
    }

    [GeneratedRegex("^#([0-9A-Fa-f]{6})$")]
    private static partial Regex HexRegex();
}
