using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop.Layout;

/// <summary>Un controllo creato a runtime per un campo personalizzato: i campi li aggiunge l'Admin dal web, quindi
/// non possono stare nel XAML della finestra.</summary>
public sealed record CustomFieldControl(string Key, string FieldType, FrameworkElement Input);

/// <summary>Costruisce a runtime i controlli dei campi personalizzati di una schermata e ne legge/valida i valori,
/// con la stessa logica del web (<c>Customers.razor</c>) e dello stesso controllo lato server
/// (<c>CustomFieldService</c>) per la stessa schermata.</summary>
public static class CustomFieldForm
{
    public static async Task<IReadOnlyList<CustomFieldDefinitionDto>> LoadAsync(ApiClient api, string screen)
    {
        try
        {
            return await api.GetCustomFieldDefinitionsAsync(screen);
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or InvalidOperationException)
        {
            // Nessun campo personalizzato, o non raggiungibile: il modulo resta con i soli campi standard.
            return [];
        }
    }

    /// <summary>Aggiunge al pannello un'etichetta e un controllo per ogni campo, nell'ordine dato: TextBox per
    /// Text/Number, DatePicker per Date, CheckBox per Checkbox. Precompila il valore già salvato per il record,
    /// se passato (modifica).</summary>
    public static List<CustomFieldControl> BuildControls(
        Panel container, IReadOnlyList<CustomFieldDefinitionDto> fields, IReadOnlyDictionary<string, string?>? values)
    {
        container.Children.Clear();
        var controls = new List<CustomFieldControl>();
        foreach (var field in fields.OrderBy(f => f.Order))
        {
            var existing = values?.GetValueOrDefault(field.Key);
            var label = new TextBlock
            {
                Text = field.Required ? field.Label + " *" : field.Label,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 5),
            };
            if (container.TryFindResource("TextSecondaryBrush") is System.Windows.Media.Brush brush)
            {
                label.Foreground = brush;
            }

            container.Children.Add(label);

            FrameworkElement input = field.FieldType switch
            {
                "Checkbox" => new CheckBox { IsChecked = bool.TryParse(existing, out var b) && b, Margin = new Thickness(0, 0, 0, 14) },
                "Date" => new DatePicker
                {
                    SelectedDate = DateTime.TryParse(existing, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                    Margin = new Thickness(0, 0, 0, 14),
                },
                _ => new TextBox { Text = existing ?? string.Empty, Margin = new Thickness(0, 0, 0, 14) },
            };

            container.Children.Add(input);
            controls.Add(new CustomFieldControl(field.Key, field.FieldType, input));
        }

        return controls;
    }

    public static string? GetValue(CustomFieldControl control) => control.FieldType switch
    {
        "Checkbox" => ((CheckBox)control.Input).IsChecked == true ? bool.TrueString : bool.FalseString,
        "Date" => ((DatePicker)control.Input).SelectedDate?.ToString("yyyy-MM-dd"),
        _ => ((TextBox)control.Input).Text,
    };

    public static Dictionary<string, string?> ToValues(IEnumerable<CustomFieldControl> controls) =>
        controls.ToDictionary(c => c.Key, GetValue);

    /// <summary>Etichette dei campi obbligatori rimasti vuoti (un Checkbox ha sempre un valore, "True" o
    /// "False": non risulta mai vuoto).</summary>
    public static List<string> Missing(IReadOnlyList<CustomFieldDefinitionDto> fields, IReadOnlyDictionary<string, string?> values) =>
        fields.Where(f => f.Required && string.IsNullOrWhiteSpace(values.GetValueOrDefault(f.Key))).Select(f => f.Label).ToList();

    /// <summary>Stesso controllo di formato del server (<c>CustomFieldService.ValidateAndStageAsync</c>) su
    /// Number e Date, per un errore immediato invece di aspettare la risposta dell'API.</summary>
    public static string? Validate(IReadOnlyList<CustomFieldDefinitionDto> fields, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var field in fields)
        {
            var value = values.GetValueOrDefault(field.Key);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (field.FieldType == "Number" && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                return $"Il campo '{field.Label}' deve essere un numero.";
            }

            if (field.FieldType == "Date" && !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return $"Il campo '{field.Label}' deve essere una data valida.";
            }
        }

        return null;
    }
}
