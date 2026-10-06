using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

/// <summary>Strumento Layout nel programma: l'Admin sceglie un modulo e ne imposta etichette, ordine, visibilità e
/// obbligatorietà. Stesse regole e stessi dati del web (/layout), perché entrambi leggono la stessa API.</summary>
public partial class LayoutEditorWindow : Window
{
    private readonly ApiClient _apiClient;

    /// <summary>Riga modificabile dalla griglia. Le regole di protezione (campo non nascondibile, obbligatorio per
    /// default) arrivano dal server e vengono solo mostrate: il server le fa rispettare comunque.</summary>
    public sealed class EditableField : INotifyPropertyChanged
    {
        private int _order;
        private string _label;
        private bool _visible;
        private bool _required;

        public EditableField(LayoutFieldDto f)
        {
            Key = f.Key;
            DefaultLabel = f.DefaultLabel;
            _label = f.Label;
            _order = f.Order;
            _visible = f.Visible;
            _required = f.Required;
            CanHide = f.CanHide;
            DefaultRequired = f.DefaultRequired;
            // Un campo obbligatorio per default che non si può nascondere resta obbligatorio.
            RequiredEditable = CanHide || !DefaultRequired;
        }

        public string Key { get; }
        public string DefaultLabel { get; }
        public bool CanHide { get; }
        public bool DefaultRequired { get; }
        public bool RequiredEditable { get; }

        public int Order { get => _order; set { _order = value; Changed(nameof(Order)); } }
        public string Label { get => _label; set { _label = value; Changed(nameof(Label)); } }
        public bool Visible { get => _visible; set { _visible = value; Changed(nameof(Visible)); } }
        public bool Required { get => _required; set { _required = value; Changed(nameof(Required)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public LayoutFieldDto ToDto() => new(Key, DefaultLabel, Label, Order, Visible, Required, CanHide, DefaultRequired);
    }

    public LayoutEditorWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var screens = await _apiClient.GetLayoutScreensAsync();
            ScreenCombo.ItemsSource = screens;
            ScreenCombo.SelectedIndex = screens.Count > 0 ? 0 : -1;
            if (_apiClient.CurrentRole == "Admin")
            {
                await LoadAccessAsync();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    /// <summary>Solo l'Admin vede la scelta dei ruoli autorizzati: le regole le fa rispettare comunque il server.</summary>
    private async Task LoadAccessAsync()
    {
        var access = await _apiClient.GetLayoutAccessAsync();
        RolesPanel.Children.Clear();
        foreach (var role in access.GrantableRoles)
        {
            RolesPanel.Children.Add(new CheckBox
            {
                Content = role,
                Tag = role,
                IsChecked = access.GrantedRoles.Contains(role),
                Margin = new Thickness(0, 0, 18, 0),
            });
        }

        AccessPanel.Visibility = Visibility.Visible;
    }

    private async void SaveAccess_Click(object sender, RoutedEventArgs e)
    {
        var roles = RolesPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
        try
        {
            await _apiClient.SaveLayoutAccessAsync(roles);
            ErrorText.Text = "Autorizzazioni salvate.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void ScreenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScreenCombo.SelectedItem is not LayoutScreenDto screen)
        {
            return;
        }

        FieldsList.ItemsSource = screen.Fields.OrderBy(f => f.Order).Select(f => new EditableField(f)).ToList();
        ErrorText.Text = string.Empty;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ScreenCombo.SelectedItem is not LayoutScreenDto screen || FieldsList.ItemsSource is not IEnumerable<EditableField> rows)
        {
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.SaveLayoutScreenAsync(screen.Screen, rows.Select(r => r.ToDto()));
            ErrorText.Text = "Layout salvato: vale da subito per tutti gli utenti.";
            await LoadAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (ScreenCombo.SelectedItem is not LayoutScreenDto screen)
        {
            return;
        }

        try
        {
            await _apiClient.SaveLayoutScreenAsync(screen.Screen, []);
            ErrorText.Text = "Valori predefiniti ripristinati.";
            await LoadAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
