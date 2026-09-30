using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>Picks a customer's issued, not yet invoiced DDTs and creates the deferred invoice draft.</summary>
public partial class InvoiceFromDocumentsWindow : Window
{
    private readonly ApiClient _apiClient;
    private IReadOnlyList<UninvoicedDocumentDto> _documents = [];

    public InvoiceDto? CreatedInvoice { get; private set; }

    public InvoiceFromDocumentsWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) =>
        {
            try
            {
                _documents = await _apiClient.GetUninvoicedDocumentsAsync();
                var customers = _documents.GroupBy(d => d.CustomerId)
                    .Select(g => new CustomerOption(g.Key, $"{g.First().CustomerName} ({g.Count()} DDT)"))
                    .OrderBy(c => c.Name).ToList();
                CustomerCombo.ItemsSource = customers;
                if (customers.Count == 0)
                {
                    ErrorText.Text = "Nessun DDT emesso da fatturare (servono DDT con un cliente in anagrafica).";
                    CreateButton.IsEnabled = false;
                }
                else
                {
                    CustomerCombo.SelectedIndex = 0;
                }
            }
            catch (Exception exception)
            {
                ErrorText.Text = exception.Message;
            }
        };
    }

    private void CustomerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomerCombo.SelectedItem is CustomerOption customer)
        {
            DocumentsList.ItemsSource = _documents.Where(d => d.CustomerId == customer.Id).Select(d => new DocumentOption(d)).ToList();
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var chosen = (DocumentsList.ItemsSource as IEnumerable<DocumentOption>)?.Where(d => d.IsChecked).Select(d => d.Id).ToList() ?? [];
        if (chosen.Count == 0)
        {
            ErrorText.Text = "Spunta almeno un DDT.";
            return;
        }

        CreateButton.IsEnabled = false;
        try
        {
            CreatedInvoice = await _apiClient.CreateInvoiceFromDocumentsAsync(chosen);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            CreateButton.IsEnabled = true;
        }
    }

    public sealed record CustomerOption(Guid Id, string Name);

    public sealed class DocumentOption(UninvoicedDocumentDto document)
    {
        public Guid Id { get; } = document.Id;
        public string DocumentCode { get; } = $"DDT {document.DocumentCode}";
        public string DateText { get; } = document.IssuedAt is { } date ? $" del {date.ToLocalTime():dd/MM/yyyy}" : string.Empty;
        public string Reason { get; } = $" · {document.Reason}";
        public bool IsChecked { get; set; } = true;
    }
}
