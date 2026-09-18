using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Maps a workflow status string (Draft, Confirmed, Received, Open, ...) to a pill background/foreground brush.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, (string Bg, string Fg)> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Draft"] = ("#E2E8F0", "#475569"),
        ["Open"] = ("#FEF3C7", "#B45309"),
        ["Ordered"] = ("#DBEAFE", "#1D4ED8"),
        ["Ready"] = ("#DBEAFE", "#1D4ED8"),
        ["Confirmed"] = ("#DBEAFE", "#1D4ED8"),
        ["PartiallyReceived"] = ("#FEF3C7", "#B45309"),
        ["Received"] = ("#DCFCE7", "#15803D"),
        ["Resolved"] = ("#DCFCE7", "#15803D"),
        ["Closed"] = ("#DCFCE7", "#15803D"),
        ["Cancelled"] = ("#FEE2E2", "#B91C1C"),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? string.Empty;
        var (bg, fg) = Palette.TryGetValue(key, out var colors) ? colors : ("#F1F5F9", "#475569");
        var hex = parameter as string == "Foreground" ? fg : bg;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

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

/// <summary>Picks a pill brush for a boolean value. Parameter "warning" makes true render amber instead of green.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTrue = value is true;
        var isForeground = parameter is string p && p.Contains("Foreground");
        var warning = parameter is string wp && wp.Contains("warning");

        string hex;
        if (!isTrue)
        {
            hex = isForeground ? "#64748B" : "#F1F5F9";
        }
        else if (warning)
        {
            hex = isForeground ? "#B45309" : "#FEF3C7";
        }
        else
        {
            hex = isForeground ? "#15803D" : "#DCFCE7";
        }

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
