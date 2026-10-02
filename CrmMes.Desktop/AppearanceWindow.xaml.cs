using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CrmMes.Desktop;

/// <summary>Admin tool to pick company colours / density and push them to the API (same theme the web uses).</summary>
public partial class AppearanceWindow : Window
{
    private readonly ApiClient _apiClient;
    private UiThemeDto? _theme;
    private readonly Dictionary<string, TextBox> _colorBoxes = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading = true;
    private string _preset = "officina";

    private static readonly (string Key, string Label)[] ColorFields =
    [
        ("Background", "Sfondo pagina"),
        ("Surface", "Superficie"),
        ("SurfaceRaised", "Superficie sollevata"),
        ("Ink", "Testo"),
        ("Muted", "Testo secondario"),
        ("Line", "Bordi"),
        ("Accent", "Accento"),
        ("AccentHover", "Accento hover"),
        ("AccentSoft", "Accento soft"),
        ("OnAccent", "Testo su accento"),
        ("Sidebar", "Menu laterale"),
        ("SidebarText", "Testo menu"),
        ("Ok", "OK / successo"),
        ("Warn", "Avviso"),
    ];

    public AppearanceWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _theme = await _apiClient.GetUiThemeAsync();
            _preset = _theme.Preset;
            BuildPresets(_theme);
            BuildColorFields();
            ApplyToForm(_theme);
            StatusText.Text = "Modifica i colori e salva per tutta l'azienda.";
            SaveButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            SaveButton.IsEnabled = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private void BuildPresets(UiThemeDto theme)
    {
        PresetsPanel.Children.Clear();
        foreach (var preset in theme.Presets)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Children =
                            {
                                Swatch(preset.Background),
                                Swatch(preset.Accent),
                            }
                        },
                        new TextBlock { Text = preset.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) },
                        new TextBlock
                        {
                            Text = preset.Description,
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = (Brush)FindResource("TextSecondaryBrush"),
                            MaxWidth = 180,
                        },
                    }
                },
                Style = (Style)FindResource("SecondaryButton"),
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(12, 10, 12, 10),
                Tag = preset,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += Preset_Click;
            PresetsPanel.Children.Add(button);
        }
    }

    private static Rectangle Swatch(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        return new Rectangle
        {
            Width = 22,
            Height = 22,
            Fill = brush,
            Stroke = Brushes.Gray,
            StrokeThickness = 1,
            Margin = new Thickness(0, 0, 4, 0),
        };
    }

    private void BuildColorFields()
    {
        ColorsGrid.Children.Clear();
        _colorBoxes.Clear();
        foreach (var (key, label) in ColorFields)
        {
            var box = new TextBox { Tag = key, Margin = new Thickness(0, 4, 0, 0) };
            box.TextChanged += Color_Changed;
            _colorBoxes[key] = box;
            var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 10) };
            stack.Children.Add(new TextBlock { Text = label });
            stack.Children.Add(box);
            ColorsGrid.Children.Add(stack);
        }
    }

    private void ApplyToForm(UiThemeDto t)
    {
        _loading = true;
        SetColor("Background", t.Background);
        SetColor("Surface", t.Surface);
        SetColor("SurfaceRaised", t.SurfaceRaised);
        SetColor("Ink", t.Ink);
        SetColor("Muted", t.Muted);
        SetColor("Line", t.Line);
        SetColor("Accent", t.Accent);
        SetColor("AccentHover", t.AccentHover);
        SetColor("AccentSoft", t.AccentSoft);
        SetColor("OnAccent", t.OnAccent);
        SetColor("Sidebar", t.Sidebar);
        SetColor("SidebarText", t.SidebarText);
        SetColor("Ok", t.Ok);
        SetColor("Warn", t.Warn);
        RadiusSlider.Value = t.Radius;
        HeightSlider.Value = t.FieldHeight;
        SelectTag(DensityBox, t.Density);
        SelectTag(BgStyleBox, t.BackgroundStyle);
        SelectTag(BorderBox, t.FieldBorder.ToString());
        RadiusValue.Text = $"{t.Radius}";
        HeightValue.Text = $"{t.FieldHeight}";
        _loading = false;
        ThemeApplier.Apply(t);
    }

    private void ApplyToForm(UiThemePresetDto p)
    {
        ApplyToForm(new UiThemeDto(
            p.Key, p.Background, p.Surface, p.SurfaceRaised, p.Ink, p.Muted, p.Line,
            p.Accent, p.AccentHover, p.AccentSoft, p.OnAccent, p.Sidebar, p.SidebarText, p.Ok, p.Warn,
            p.Radius, p.Density, p.BackgroundStyle, p.FieldBorder, p.FieldHeight,
            [], _theme?.Presets ?? []));
        _preset = p.Key;
    }

    private void SetColor(string key, string value)
    {
        if (_colorBoxes.TryGetValue(key, out var box))
        {
            box.Text = value;
        }
    }

    private static void SelectTag(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: UiThemePresetDto preset })
        {
            ApplyToForm(preset);
            StatusText.Text = $"Palette «{preset.Name}» in anteprima — premi Salva per applicarla.";
        }
    }

    private void Color_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        PreviewLocal();
    }

    private void Shape_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        RadiusValue.Text = $"{(int)RadiusSlider.Value}";
        HeightValue.Text = $"{(int)HeightSlider.Value}";
        PreviewLocal();
    }

    private void PreviewLocal()
    {
        try
        {
            ThemeApplier.Apply(BuildDto());
        }
        catch
        {
            // Incomplete hex while typing.
        }
    }

    private UiThemeDto BuildDto()
    {
        string C(string key) => _colorBoxes.TryGetValue(key, out var box) ? box.Text.Trim() : "#000000";
        string Tag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        return new UiThemeDto(
            _preset, C("Background"), C("Surface"), C("SurfaceRaised"), C("Ink"), C("Muted"), C("Line"),
            C("Accent"), C("AccentHover"), C("AccentSoft"), C("OnAccent"), C("Sidebar"), C("SidebarText"),
            C("Ok"), C("Warn"), (int)RadiusSlider.Value, Tag(DensityBox), Tag(BgStyleBox),
            int.TryParse(Tag(BorderBox), out var border) ? border : 1, (int)HeightSlider.Value,
            [], _theme?.Presets ?? []);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        StatusText.Text = "Salvataggio...";
        try
        {
            var draft = BuildDto();
            _theme = await _apiClient.SaveUiThemeAsync(new SaveUiThemeDto(
                null, draft.Preset, draft.Background, draft.Surface, draft.SurfaceRaised, draft.Ink, draft.Muted, draft.Line,
                draft.Accent, draft.AccentHover, draft.AccentSoft, draft.OnAccent, draft.Sidebar, draft.SidebarText,
                draft.Ok, draft.Warn, draft.Radius, draft.Density, draft.BackgroundStyle, draft.FieldBorder, draft.FieldHeight));
            ThemeApplier.Apply(_theme);
            StatusText.Text = "Aspetto salvato per tutta l'azienda.";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
