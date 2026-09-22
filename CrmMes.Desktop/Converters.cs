using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Maps a workflow status string (Draft, Confirmed, Received, Open, ...) to the telemetry-style
/// status tag's color. There is no background fill in this design language (see the "Pill" style) — every
/// status reads as colored text against the panel, like a terminal readout, not a colored badge.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Draft"] = "#8C8C92",
        ["Pending"] = "#8C8C92",
        ["Open"] = "#FFB84D",
        ["PartiallyReceived"] = "#FFB84D",
        ["InProgress"] = "#FFB84D",
        ["Ordered"] = "#D6FF3F",
        ["Ready"] = "#D6FF3F",
        ["Confirmed"] = "#D6FF3F",
        ["Released"] = "#D6FF3F",
        ["Received"] = "#7CE0A8",
        ["Resolved"] = "#7CE0A8",
        ["Closed"] = "#7CE0A8",
        ["Completed"] = "#7CE0A8",
        ["Done"] = "#7CE0A8",
        ["Cancelled"] = "#FF5C5C",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter as string != "Foreground")
        {
            return Brushes.Transparent;
        }

        var key = value as string ?? string.Empty;
        var hex = Palette.TryGetValue(key, out var color) ? color : "#8C8C92";
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Translates a workflow status string (as stored/returned by the API, always in English:
/// Draft, Released, Done, ...) to the Italian label shown in the UI. Centralized here so every pill,
/// column and detail header stays consistent instead of each screen inventing its own text.</summary>
public sealed class StatusToItalianTextConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Draft"] = "Bozza",
        ["Open"] = "Aperto",
        ["Ordered"] = "Ordinato",
        ["Ready"] = "Pronta",
        ["Confirmed"] = "Confermato",
        ["PartiallyReceived"] = "Ricevuto parzialmente",
        ["Received"] = "Ricevuto",
        ["Resolved"] = "Risolto",
        ["Closed"] = "Chiusa",
        ["Cancelled"] = "Annullata",
        ["Released"] = "Rilasciata",
        ["InProgress"] = "In corso",
        ["Completed"] = "Completata",
        ["Pending"] = "In attesa",
        ["Done"] = "Completata",
    };

    public static string Translate(string? status) =>
        status is not null && Labels.TryGetValue(status, out var label) ? label : status ?? string.Empty;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Translate(value as string);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Renders a boolean as a short localized label, e.g. true/false -> "Sì"/"No". Parameter: "TrueText|FalseText".</summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "Sì|No").Split('|');
        var isTrue = value is true;
        return isTrue ? parts[0] : parts.Length > 1 ? parts[1] : "No";
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Picks the status-tag color for a boolean value. No fill (see "Pill" style) — only the text
/// color changes. Parameter "warning" makes true render amber instead of lime.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTrue = value is true;
        var isForeground = parameter is string p && p.Contains("Foreground");
        var warning = parameter is string wp && wp.Contains("warning");

        if (!isForeground)
        {
            return Brushes.Transparent;
        }

        var hex = !isTrue ? "#8C8C92" : warning ? "#FFB84D" : "#D6FF3F";
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Labels the action button for a work order operation: "Avvia" while Pending, "Completa"
/// while InProgress, nothing once Done (the button is then hidden by <see cref="StatusNotEqualToVisibilityConverter"/>).</summary>
public sealed class OperationStatusToActionTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "Pending" => "Avvia",
            "InProgress" => "Completa",
            _ => string.Empty
        };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Collapses an element when the bound status string equals the converter parameter, e.g.
/// hiding the start/complete button once an operation reaches "Done".</summary>
public sealed class StatusNotEqualToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
