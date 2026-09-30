using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>The Admin's channel settings (see AccessChannels in CrmMes.Api): channels the company uses,
/// channels each role may log in from, channels each area is shown on. Built from the server's lists of
/// roles and areas, so a new module appears here without changing this window.</summary>
public partial class AccessChannelsWindow : Window
{
    private static readonly string[] Channels = ["desktop", "web", "mobile"];
    private static readonly string[] AreaChannels = ["desktop", "web"];

    private static readonly Dictionary<string, string> ChannelLabels = new()
    {
        ["desktop"] = "Desktop",
        ["web"] = "Web",
        ["mobile"] = "Telefono",
    };

    private static readonly Dictionary<string, string> RoleLabels = new()
    {
        ["Admin"] = "Amministratore",
        ["Management"] = "Direzione",
        ["Sales"] = "Commerciale",
        ["Purchasing"] = "Acquisti",
        ["Warehouse"] = "Magazzino",
        ["Operator"] = "Operatore",
    };

    private readonly ApiClient _apiClient;
    private readonly Dictionary<(string Role, string Channel), CheckBox> _roleBoxes = [];
    private readonly Dictionary<(string Area, string Channel), CheckBox> _areaBoxes = [];

    public bool Saved { get; private set; }

    public AccessChannelsWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var settings = await _apiClient.GetAccessChannelsAsync();
            DesktopChannelBox.IsChecked = settings.Channels.Contains("desktop");
            WebChannelBox.IsChecked = settings.Channels.Contains("web");
            MobileChannelBox.IsChecked = settings.Channels.Contains("mobile");

            BuildMatrix(RolesGrid, "Ruolo", settings.KnownRoles.Select(role => (role, RoleLabels.GetValueOrDefault(role, role))).ToList(),
                Channels, (role, channel) => !settings.Roles.TryGetValue(role, out var allowed) || allowed.Contains(channel),
                _roleBoxes, role => role == "Admin");
            BuildMatrix(AreasGrid, "Area", settings.KnownAreas.Select(area => (area.Key, area.Name)).ToList(),
                AreaChannels, (area, channel) => !settings.Areas.TryGetValue(area, out var shown) || shown.Contains(channel),
                _areaBoxes, _ => false);

            ApplyChannelAvailability();
            StatusText.Text = string.Empty;
            SaveButton.IsEnabled = true;
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    /// <summary>One row per role or area, one checkbox column per channel. Rows marked fixed (the Admin)
    /// are shown checked and locked: that role always has every active channel.</summary>
    private static void BuildMatrix(
        Grid grid,
        string firstHeader,
        List<(string Key, string Label)> rows,
        IReadOnlyList<string> channels,
        Func<string, string, bool> isChecked,
        Dictionary<(string, string), CheckBox> boxes,
        Func<string, bool> isFixed)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var _ in channels)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddHeader(grid, firstHeader, 0);
        for (var c = 0; c < channels.Count; c++)
        {
            AddHeader(grid, ChannelLabels[channels[c]], c + 1);
        }

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (key, label) = rows[r];
            var fixedRow = isFixed(key);
            var text = new TextBlock
            {
                Text = fixedRow ? $"{label} (sempre tutti i canali attivi)" : label,
                Margin = new Thickness(0, 6, 8, 6),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            Grid.SetRow(text, r + 1);
            grid.Children.Add(text);

            for (var c = 0; c < channels.Count; c++)
            {
                var box = new CheckBox
                {
                    IsChecked = fixedRow || isChecked(key, channels[c]),
                    IsEnabled = !fixedRow,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = $"{label}: {ChannelLabels[channels[c]]}",
                };
                Grid.SetRow(box, r + 1);
                Grid.SetColumn(box, c + 1);
                grid.Children.Add(box);
                if (!fixedRow)
                {
                    boxes[(key, channels[c])] = box;
                }
            }
        }
    }

    private static void AddHeader(Grid grid, string text, int column)
    {
        var header = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center,
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(header, column);
        grid.Children.Add(header);
    }

    private void ChannelBox_Click(object sender, RoutedEventArgs e) => ApplyChannelAvailability();

    /// <summary>A channel the company doesn't use can't be granted to a role or area: its column is locked.</summary>
    private void ApplyChannelAvailability()
    {
        var active = ActiveChannels();
        foreach (var ((_, channel), box) in _roleBoxes)
        {
            box.IsEnabled = active.Contains(channel);
        }

        foreach (var ((_, channel), box) in _areaBoxes)
        {
            box.IsEnabled = active.Contains(channel);
        }
    }

    private List<string> ActiveChannels()
    {
        var active = new List<string>();
        if (DesktopChannelBox.IsChecked == true) active.Add("desktop");
        if (WebChannelBox.IsChecked == true) active.Add("web");
        if (MobileChannelBox.IsChecked == true) active.Add("mobile");
        return active;
    }

    /// <summary>Only what differs from "everything" is stored: a role or area with every channel ticked
    /// is left out, so channels switched on later apply to it automatically.</summary>
    internal static Dictionary<string, List<string>> ToRestrictions(
        Dictionary<(string Key, string Channel), bool> ticks, IReadOnlyList<string> channels)
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var key in ticks.Keys.Select(k => k.Key).Distinct())
        {
            var allowed = channels.Where(channel => ticks.TryGetValue((key, channel), out var on) && on).ToList();
            if (allowed.Count < channels.Count)
            {
                result[key] = allowed;
            }
        }

        return result;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var channels = ActiveChannels();
        if (!channels.Contains("desktop") && !channels.Contains("web"))
        {
            StatusText.Text = "Serve almeno il programma desktop o la piattaforma web.";
            return;
        }

        if (!channels.Contains("desktop"))
        {
            var answer = MessageBox.Show(this,
                "Spegni il programma desktop: dopo il salvataggio solo l'amministratore potrà ancora usarlo, gli altri useranno la piattaforma web. Continuare?",
                "Canali di accesso", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var roles = ToRestrictions(_roleBoxes.ToDictionary(pair => pair.Key, pair => pair.Value.IsChecked == true), Channels);
        var areas = ToRestrictions(_areaBoxes.ToDictionary(pair => pair.Key, pair => pair.Value.IsChecked == true), AreaChannels);

        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.SaveAccessChannelsAsync(new SaveAccessChannelsDto(channels, roles, areas));
            Saved = true;
            Close();
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
