using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop.Layout;

/// <summary>Un controllo di una finestra associato a un campo dello strumento Layout. L'etichetta è il
/// <see cref="TextBlock"/> che lo precede nello stesso pannello: non serve nominarla a parte.</summary>
public sealed record FieldBinding(string Key, FrameworkElement Control);

/// <summary>Applica alle finestre WPF la configurazione salvata dall'Admin nello strumento Layout: nasconde i
/// campi, cambia l'etichetta e segna gli obbligatori. Le stesse regole del web, per la stessa schermata.</summary>
public static class FormLayoutApplier
{
    public static async Task<IReadOnlyList<LayoutFieldDto>> LoadAsync(ApiClient api, string screen)
    {
        try
        {
            return (await api.GetLayoutScreenAsync(screen)).Fields;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or InvalidOperationException)
        {
            // Senza layout la finestra resta con i valori di default: l'inserimento non si blocca.
            return [];
        }
    }

    public static void Apply(IReadOnlyList<LayoutFieldDto> fields, IEnumerable<FieldBinding> bindings)
    {
        if (fields.Count == 0)
        {
            return;
        }

        var byKey = fields.ToDictionary(f => f.Key);
        foreach (var binding in bindings)
        {
            if (!byKey.TryGetValue(binding.Key, out var field))
            {
                continue;
            }

            binding.Control.Visibility = field.Visible ? Visibility.Visible : Visibility.Collapsed;
            var label = PrecedingLabel(binding.Control);
            if (label is not null)
            {
                label.Text = LabelText(field);
                label.Visibility = binding.Control.Visibility;
            }
        }
    }

    /// <summary>Nomi dei campi obbligatori visibili che risultano vuoti.</summary>
    public static List<string> Missing(IReadOnlyList<LayoutFieldDto> fields, Func<string, bool> hasValue)
    {
        return fields.Where(f => f.Visible && f.Required && !hasValue(f.Key)).Select(f => f.Label).ToList();
    }

    public static string LabelText(LayoutFieldDto field) => field.Required ? field.Label + " *" : field.Label;

    private static TextBlock? PrecedingLabel(FrameworkElement control)
    {
        if (control.Parent is not Panel panel)
        {
            return null;
        }

        var index = panel.Children.IndexOf(control);
        return index > 0 ? panel.Children[index - 1] as TextBlock : null;
    }
}
