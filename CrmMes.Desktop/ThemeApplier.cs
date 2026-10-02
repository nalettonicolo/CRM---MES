using System.Windows;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Applies the company <c>UiTheme</c> from the API onto application brushes at runtime.
/// Styles use DynamicResource for these keys so colour changes take effect without restart.</summary>
public static class ThemeApplier
{
    public static void Apply(UiThemeDto theme)
    {
        if (theme is null || Application.Current?.Resources is null)
        {
            return;
        }

        Set("BackgroundBrush", theme.Background);
        Set("SurfaceBrush", theme.Surface);
        Set("SurfaceRaisedBrush", theme.SurfaceRaised);
        Set("BorderBrush0", theme.Line);
        Set("TextPrimaryBrush", theme.Ink);
        Set("TextSecondaryBrush", theme.Muted);
        Set("TextMutedBrush", theme.Muted);
        Set("PrimaryBrush", theme.Accent);
        Set("PrimaryHoverBrush", theme.AccentHover);
        Set("PrimaryPressedBrush", Darken(theme.AccentHover, 0.15));
        Set("PrimarySoftBrush", theme.AccentSoft);
        Set("OnAccentTextBrush", theme.OnAccent);
        Set("DangerBrush", theme.Accent);
        Set("DangerHoverBrush", theme.AccentHover);
        Set("SuccessTextBrush", theme.Ok);
        Set("WarningTextBrush", theme.Warn);
        Set("RowSelectedBrush", theme.AccentSoft);
        Set("SidebarBgBrush", theme.Sidebar);
        Set("SidebarTextBrush", theme.SidebarText);
        Set("TitleBarBgBrush", theme.Sidebar);
        Set("TitleBarTextBrush", "#F5F5F1");
        Set("TitleBarCloseHoverBrush", theme.Accent);
        Set("OutlineBgBrush", theme.Surface);
        Set("OutlineHoverBgBrush", theme.Background);
        Set("OutlineBorderBrush", theme.Line);
        Set("HeaderBgBrush", theme.Surface);
        Set("RowHoverBrush", theme.SurfaceRaised);
        Set("RowAlternateBrush", theme.SurfaceRaised);
    }

    private static void Set(string key, string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
        catch (FormatException)
        {
            // Keep previous brush if the server sent a bad value.
        }
    }

    private static string Darken(string hex, double amount)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex)!;
            byte D(byte channel) => (byte)Math.Clamp((int)(channel * (1 - amount)), 0, 255);
            return $"#{D(c.R):X2}{D(c.G):X2}{D(c.B):X2}";
        }
        catch (FormatException)
        {
            return hex;
        }
    }
}
