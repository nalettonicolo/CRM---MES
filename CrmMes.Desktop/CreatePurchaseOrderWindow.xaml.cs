using System.Collections.ObjectModel;
using System.Windows;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

public partial class CreatePurchaseOrderWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid? _editingOrderId;
    private readonly ObservableCollection<ItemRow> _items = new();

    public bool Created { get; private set; }

    private IReadOnlyList<LayoutFieldDto> _layout = [];

    /// <summary>Creation mode.</summary>
    public CreatePurchaseOrderWindow(ApiClient apiClient, IReadOnlyList<SupplierDto> suppliers)
        : this(apiClient, suppliers, existing: null)
    {
    }

    /// <summary>Edit mode: pre-fills supplier and items from <paramref name="existing"/>.</summary>
    public CreatePurchaseOrderWindow(ApiClient apiClient, IReadOnlyList<SupplierDto> suppliers, PurchaseOrderDetailDto? existing)
    {
        InitializeComponent();
        _apiClient = apiClient;
        SupplierCombo.ItemsSource = suppliers;

        if (existing is null)
        {
            Loaded += async (_, _) => await LoadLayoutAsync();
        }

        if (existing is not null)
        {
            _editingOrderId = existing.Id;
            Title = $"Modifica ordine {existing.Code}";
            CreateButton.Content = "Salva modifiche";
            SupplierCombo.SelectedItem = suppliers.FirstOrDefault(supplier => supplier.Id == existing.SupplierId);
            foreach (var item in existing.Items)
            {
                _items.Add(new ItemRow(item.MaterialCode, item.Quantity, item.UnitPrice));
            }
        }
        else if (suppliers.Count > 0)
        {
            SupplierCombo.SelectedIndex = 0;
        }

        ItemsList.ItemsSource = _items;
    }

    /// <summary>Layout dell'ordine in creazione: fornitore, e per le righe le intestazioni di colonna
    /// (codice, quantità, prezzo) con visibilità e obbligatorietà scelte dall'Admin.</summary>
    private async Task LoadLayoutAsync()
    {
        _layout = await FormLayoutApplier.LoadAsync(_apiClient, "purchaseOrder.new");
        FormLayoutApplier.Apply(_layout,
        [
            new FieldBinding("supplier", SupplierCombo, SupplierLabel),
            new FieldBinding("itemCode", ItemCodeBox, ItemCodeLabel),
            new FieldBinding("itemQuantity", ItemQuantityBox, ItemQuantityLabel),
            new FieldBinding("itemPrice", ItemPriceBox, ItemPriceLabel),
        ]);
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var code = ItemCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) ||
            !NumberInput.TryParseDecimal(ItemQuantityBox.Text, out var quantity) || quantity <= 0 ||
            !NumberInput.TryParseDecimal(ItemPriceBox.Text, out var unitPrice) || unitPrice < 0)
        {
            ErrorText.Text = "Inserisci un codice materiale, una quantità e un prezzo validi.";
            return;
        }

        _items.Add(new ItemRow(code, quantity, unitPrice));
        ItemCodeBox.Text = string.Empty;
        ItemQuantityBox.Text = "1";
        ItemPriceBox.Text = "0";
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
        if (SupplierCombo.SelectedItem is not SupplierDto supplier)
        {
            ErrorText.Text = "Seleziona un fornitore.";
            return;
        }

        if (_items.Count == 0)
        {
            ErrorText.Text = "Aggiungi almeno una riga materiale.";
            return;
        }

        CreateButton.IsEnabled = false;
        try
        {
            if (_editingOrderId is Guid orderId)
            {
                await _apiClient.EditPurchaseOrderAsync(
                    orderId,
                    supplier.Id,
                    _items.Select(item => (item.MaterialCode, item.Quantity, item.UnitPrice)).ToList());
            }
            else
            {
                await _apiClient.CreatePurchaseOrderAsync(
                    supplier.Id,
                    _items.Select(item => (item.MaterialCode, item.Quantity, item.UnitPrice)).ToList());
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

    private sealed record ItemRow(string MaterialCode, decimal Quantity, decimal UnitPrice);
}
