using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

/// <summary>Creates a quote, or edits a Draft one. Lines tied to a product become work orders when the
/// quote is converted; free lines (installation, transport...) are only priced. The price of a product
/// line can be estimated from its bill of materials (cheapest known catalog price per material) plus a
/// markup, always shown with how many materials had no price.</summary>
public partial class QuoteEditorWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly QuoteDto? _existing;
    private readonly ObservableCollection<QuoteLineRow> _lines = new();

    public bool Created { get; private set; }

    private IReadOnlyList<LayoutFieldDto> _layout = [];

    public QuoteEditorWindow(ApiClient apiClient, QuoteDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _existing = existing;
        LinesList.ItemsSource = _lines;
        _lines.CollectionChanged += (_, _) => UpdateTotal();

        if (existing is not null)
        {
            Title = $"Preventivo {existing.Code}";
            HeaderText.Text = $"Preventivo {existing.Code}";
            ValidUntilPicker.SelectedDate = existing.ValidUntil?.ToLocalTime();
            NotesBox.Text = existing.Notes ?? string.Empty;
            foreach (var item in existing.Items)
            {
                _lines.Add(new QuoteLineRow(
                    item.ProductId,
                    item.ProductCode is null ? "Riga libera" : $"{item.ProductCode} · {item.ProductName}",
                    item.Description, item.Quantity, item.UnitPrice, item.DiscountPercent));
            }
        }
        else
        {
            ValidUntilPicker.SelectedDate = DateTime.Today.AddDays(30);
        }

        Loaded += async (_, _) => await LoadListsAsync(existing?.CustomerId);
        if (existing is null)
        {
            Loaded += async (_, _) => await LoadLayoutAsync();
        }
    }

    /// <summary>Layout del preventivo in creazione: testata (cliente, validità, note) e colonne delle righe
    /// (descrizione, quantità, prezzo, sconto) con etichette e obbligatorietà scelte dall'Admin.</summary>
    private async Task LoadLayoutAsync()
    {
        _layout = await FormLayoutApplier.LoadAsync(_apiClient, "quote.new");
        FormLayoutApplier.Apply(_layout,
        [
            new FieldBinding("customer", CustomerCombo, CustomerLabel),
            new FieldBinding("validUntil", ValidUntilPicker, ValidUntilLabel),
            new FieldBinding("notes", NotesBox, NotesLabel),
            new FieldBinding("lineDescription", DescriptionBox, LineDescriptionLabel),
            new FieldBinding("lineQuantity", QuantityBox, LineQuantityLabel),
            new FieldBinding("linePrice", PriceBox, LinePriceLabel),
            new FieldBinding("lineDiscount", DiscountBox, LineDiscountLabel),
        ]);
    }

    private async Task LoadListsAsync(Guid? selectCustomerId)
    {
        try
        {
            var customers = await _apiClient.GetCustomersAsync();
            CustomerCombo.ItemsSource = customers;
            CustomerCombo.SelectedItem = customers.FirstOrDefault(c => c.Id == selectCustomerId);

            if (ProductCombo.ItemsSource is null)
            {
                var products = await _apiClient.GetProductsAsync();
                var options = new List<ProductOption> { new(null, "Riga libera (nessun prodotto)", null) };
                options.AddRange(products.Select(p => new ProductOption(p.Id, $"{p.Code} · {p.Name}", p.Name)));
                ProductCombo.ItemsSource = options;
                ProductCombo.SelectedIndex = 0;
            }
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void NewCustomer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomerEditWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (!dialog.Created)
        {
            return;
        }

        var before = (CustomerCombo.ItemsSource as IEnumerable<CustomerDto>)?.Select(c => c.Id).ToHashSet() ?? [];
        var customers = await _apiClient.GetCustomersAsync();
        CustomerCombo.ItemsSource = customers;
        CustomerCombo.SelectedItem = customers.FirstOrDefault(c => !before.Contains(c.Id));
    }

    private void ProductCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var option = ProductCombo.SelectedItem as ProductOption;
        EstimateButton.IsEnabled = option?.Id is not null;
        EstimateText.Text = string.Empty;
        if (option?.Name is not null && string.IsNullOrWhiteSpace(DescriptionBox.Text))
        {
            DescriptionBox.Text = option.Name;
        }
    }

    private async void Estimate_Click(object sender, RoutedEventArgs e)
    {
        if (ProductCombo.SelectedItem is not ProductOption { Id: { } productId })
        {
            return;
        }

        if (!TryParseDecimal(MarkupBox.Text, out var markup) || markup < 0)
        {
            EstimateText.Text = "Ricarico non valido.";
            return;
        }

        EstimateButton.IsEnabled = false;
        try
        {
            var cost = await _apiClient.GetProductMaterialCostAsync(productId);
            var price = Math.Round(cost.MaterialCost * (100 + markup) / 100, 2, MidpointRounding.AwayFromZero);
            PriceBox.Text = price.ToString("0.00", CultureInfo.CurrentCulture);

            var basis = $"Materiali {cost.MaterialCost.ToString("N2", CultureInfo.CurrentCulture)} € + {markup:0.##}%";
            EstimateText.Text = cost.Lines.Count == 0
                ? "Il prodotto non ha distinta base: prezzo da inserire a mano."
                : cost.MissingPriceCount > 0
                    ? $"{basis}. Attenzione: {cost.MissingPriceCount} materiali su {cost.Lines.Count} senza prezzo a listino, stima incompleta."
                    : $"{basis}. Manodopera non inclusa.";
        }
        catch (Exception exception)
        {
            EstimateText.Text = exception.Message;
        }
        finally
        {
            EstimateButton.IsEnabled = true;
        }
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        var description = DescriptionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            ErrorText.Text = "Inserisci una descrizione per la riga.";
            return;
        }

        if (!TryParseDecimal(QuantityBox.Text, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Quantità non valida.";
            return;
        }

        if (!TryParseDecimal(PriceBox.Text, out var price) || price < 0)
        {
            ErrorText.Text = "Prezzo non valido.";
            return;
        }

        if (!TryParseDecimal(string.IsNullOrWhiteSpace(DiscountBox.Text) ? "0" : DiscountBox.Text, out var discount) || discount is < 0 or > 100)
        {
            ErrorText.Text = "Lo sconto deve essere tra 0 e 100.";
            return;
        }

        var option = ProductCombo.SelectedItem as ProductOption;
        _lines.Add(new QuoteLineRow(option?.Id, option?.Id is null ? "Riga libera" : option.Label, description, quantity, price, discount));

        ErrorText.Text = string.Empty;
        EstimateText.Text = string.Empty;
        DescriptionBox.Text = string.Empty;
        QuantityBox.Text = "1";
        PriceBox.Text = string.Empty;
        DiscountBox.Text = "0";
        ProductCombo.SelectedIndex = 0;
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QuoteLineRow row })
        {
            _lines.Remove(row);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CustomerCombo.SelectedItem is not CustomerDto customer)
        {
            ErrorText.Text = "Scegli il cliente.";
            return;
        }

        if (_lines.Count == 0)
        {
            ErrorText.Text = "Aggiungi almeno una riga.";
            return;
        }

        var validUntil = ValidUntilPicker.SelectedDate is { } date ? DateTime.SpecifyKind(date.Date, DateTimeKind.Utc) : (DateTime?)null;
        var request = new SaveQuoteDto(
            customer.Id, validUntil, string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            _lines.Select(line => new SaveQuoteItemDto(line.ProductId, line.Description, line.Quantity, line.UnitPrice, line.DiscountPercent)).ToList());

        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.SaveQuoteAsync(_existing?.Id, request);
            Created = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateTotal() =>
        TotalText.Text = $"Totale {_lines.Sum(line => line.LineTotal).ToString("N2", CultureInfo.CurrentCulture)} €";

    /// <summary>Accepts both "1250,50" (Italian keyboard) and "1250.50".</summary>
    internal static bool TryParseDecimal(string text, out decimal value)
    {
        return NumberInput.TryParseDecimal(text, out value);
    }

    private sealed record ProductOption(Guid? Id, string Label, string? Name);
}

/// <summary>One line in the quote editor; <see cref="LineTotal"/> mirrors the server's rounding
/// (per line, to the cent) so the total shown before saving matches the saved one.</summary>
public sealed record QuoteLineRow(Guid? ProductId, string ProductLabel, string Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent)
{
    public decimal LineTotal => Math.Round(Quantity * UnitPrice * (100 - DiscountPercent) / 100, 2, MidpointRounding.AwayFromZero);
}
