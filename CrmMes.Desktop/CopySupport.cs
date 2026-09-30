using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CrmMes.Desktop;

/// <summary>Copying codes (work order, lot, SSCC...) out of lists: a right-click menu with one entry per
/// value, and Ctrl+C copying the first one of the selected row, so a code can be pasted into an e-mail,
/// a supplier portal or a search box instead of being retyped.</summary>
public static class CopySupport
{
    public static void Attach(ListView list, params (string Header, Func<object, string?> Value)[] values)
    {
        var menu = new ContextMenu();
        foreach (var (header, value) in values)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => Copy(list.SelectedItem is { } row ? value(row) : null);
            menu.Items.Add(item);
        }

        menu.Opened += (_, _) =>
        {
            foreach (MenuItem item in menu.Items)
            {
                item.IsEnabled = list.SelectedItem is not null;
            }
        };
        list.ContextMenu = menu;
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && list.SelectedItem is { } row)
            {
                Copy(values[0].Value(row));
                e.Handled = true;
            }
        };
    }

    public static void Copy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text.Trim());
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program is holding the clipboard: a second click works, nothing to report.
        }
    }
}
