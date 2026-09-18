using System.Collections.ObjectModel;
using System.Windows;

namespace CrmMes.Desktop;

public partial class CreateWithdrawalSlipWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _currentUserId;
    private readonly Guid? _editingSlipId;
    private readonly ObservableCollection<ItemRow> _items = new();

    public bool Created { get; private set; }

    /// <summary>Creation mode.</summary>
    public CreateWithdrawalSlipWindow(ApiClient apiClient, IReadOnlyList<AreaDto> areas, Guid currentUserId)
        : this(apiClient, areas, currentUserId, existing: null)
    {
    }

    /// <summary>Edit mode: pre-fills notes and items from <paramref name="existing"/> and disables the
    /// area picker (the API doesn't allow moving a slip to a different area after creation).</summary>
    public CreateWithdrawalSlipWindow(ApiClient apiClient, IReadOnlyList<AreaDto> areas, Guid currentUserId, WithdrawalSlipDetailDto? existing)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _currentUserId = currentUserId;
        AreaCombo.ItemsSource = areas;

        if (existing is not null)
        {
            _editingSlipId = existing.Id;
            Title = $"Modifica distinta {existing.Code}";
            CreateButton.Content = "Salva modifiche";
            AreaCombo.SelectedItem = areas.FirstOrDefault(area => area.Id == existing.AreaId);
            AreaCombo.IsEnabled = false;
            NotesBox.Text = existing.Notes;
            foreach (var item in existing.Items)
            {
                _items.Add(new ItemRow(item.MaterialCode, item.Quantity));
            }
        }
        else if (areas.Count > 0)
        {
            AreaCombo.SelectedIndex = 0;
        }

        ItemsList.ItemsSource = _items;
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var code = ItemCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) || !decimal.TryParse(ItemQuantityBox.Text, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Inserisci un codice materiale e una quantità valida.";
            return;
        }

        _items.Add(new ItemRow(code, quantity));
        ItemCodeBox.Text = string.Empty;
        ItemQuantityBox.Text = "1";
        ErrorText.Text = string.Empty;
        ItemCodeBox.Focus();
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ItemRow row })
        {
            _items.Remove(row);
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
        {
            ErrorText.Text = "Aggiungi almeno una riga materiale.";
            return;
        }

        CreateButton.IsEnabled = false;
        try
        {
            if (_editingSlipId is Guid slipId)
            {
                await _apiClient.EditWithdrawalSlipAsync(
                    slipId,
                    NotesBox.Text,
                    _items.Select(item => (item.MaterialCode, item.Quantity)).ToList());
            }
            else
            {
                if (AreaCombo.SelectedItem is not AreaDto area)
                {
                    ErrorText.Text = "Seleziona un'area.";
                    CreateButton.IsEnabled = true;
                    return;
                }

                await _apiClient.CreateWithdrawalSlipAsync(
                    area.Id,
                    _currentUserId,
                    NotesBox.Text,
                    _items.Select(item => (item.MaterialCode, item.Quantity)).ToList());
            }

            Created = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record ItemRow(string MaterialCode, decimal Quantity);
}
