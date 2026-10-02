using System.Globalization;
using System.Windows;
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
        ["Draft"] = "#757570",
        ["Pending"] = "#757570",
        ["Open"] = "#8F5A10",
        ["PartiallyReceived"] = "#8F5A10",
        ["InProgress"] = "#8F5A10",
        ["Ordered"] = "#3A5F52",
        ["Ready"] = "#3A5F52",
        ["Confirmed"] = "#3A5F52",
        ["Released"] = "#3A5F52",
        ["Received"] = "#2B5A36",
        ["Resolved"] = "#2B5A36",
        ["Closed"] = "#2B5A36",
        ["Completed"] = "#2B5A36",
        ["Done"] = "#2B5A36",
        ["Good"] = "#2B5A36",
        ["Cancelled"] = "#CF2A1F",
        ["Scrapped"] = "#CF2A1F",
        ["Preparing"] = "#757570",
        ["Shipped"] = "#3A5F52",
        ["Delivered"] = "#2B5A36",
        ["Sent"] = "#3A5F52",
        ["Accepted"] = "#2B5A36",
        ["Rejected"] = "#CF2A1F",
        ["Issued"] = "#2B5A36",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter as string != "Foreground")
        {
            return Brushes.Transparent;
        }

        var key = value as string ?? string.Empty;
        var hex = Palette.TryGetValue(key, out var color) ? color : "#757570";
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a planning entry's Type ("WorkOrder", "PurchaseOrder", "Shipment", "MaintenanceTask")
/// to a distinct color, so the planning board's entries read as "which kind of commitment" at a glance
/// without reading the row — same colored-text pattern as StatusToBrushConverter, one hue per type
/// instead of per status.</summary>
public sealed class PlanningTypeToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WorkOrder"] = "#3A5F52",
        ["PurchaseOrder"] = "#8F5A10",
        ["Shipment"] = "#2B5A36",
        ["MaintenanceTask"] = "#5C5346",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? string.Empty;
        var hex = Palette.TryGetValue(key, out var color) ? color : "#757570";
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
        ["PartiallyReceived"] = "Ricevuto in parte",
        ["Received"] = "Ricevuto",
        ["Resolved"] = "Risolto",
        ["Closed"] = "Chiusa",
        ["Cancelled"] = "Annullata",
        ["Released"] = "Rilasciata",
        ["InProgress"] = "In lavorazione",
        ["Completed"] = "Completata",
        ["Pending"] = "Da fare",
        ["Done"] = "Completata",
        ["Issued"] = "Emesso",
        ["Signed"] = "Firmato",
        ["InValutazione"] = "In valutazione",
        ["Confermata"] = "Confermata",
        ["InProduzione"] = "In produzione",
        ["Sospesa"] = "Sospesa",
        ["Consegnata"] = "Consegnata",
        ["Good"] = "Buona",
        ["Scrapped"] = "Scartata",
        ["Preparing"] = "In preparazione",
        ["Shipped"] = "Spedita",
        ["Delivered"] = "Consegnata",
        ["Inbound"] = "In ingresso",
        ["Outbound"] = "In uscita",
        ["Sent"] = "Inviato",
        ["Accepted"] = "Accettato",
        ["Rejected"] = "Rifiutato",
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

/// <summary>Shows/collapses an element based on a boolean — true means Visible unless parameter
/// "Invert" is passed, in which case true means Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTrue = value is true;
        if (parameter as string == "Invert")
        {
            isTrue = !isTrue;
        }

        return isTrue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Picks the status-tag color for a boolean value. No fill (see "Pill" style) — only the text
/// color changes. Parameter "warning" makes true render amber instead of green.</summary>
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

        var hex = !isTrue ? "#757570" : warning ? "#8F5A10" : "#2B5A36";
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
