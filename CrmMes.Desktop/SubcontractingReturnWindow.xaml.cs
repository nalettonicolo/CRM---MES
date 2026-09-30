using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>Goods coming back from the subcontractor for one line of a conto lavorazione DDT: records a
/// return (possibly partial, possibly with scrap) and lists or removes the ones already recorded.</summary>
public partial class SubcontractingReturnWindow : Window
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private readonly ApiClient _apiClient;
    private readonly Guid _documentId;
    private readonly Guid _lineId;

    /// <summary>The document as it is after the last change, null if nothing changed.</summary>
    public TransportDocumentDto? UpdatedDocument { get; private set; }

    public SubcontractingReturnWindow(ApiClient apiClient, TransportDocumentDto document, TransportDocumentLineDto line)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _documentId = document.Id;
        _lineId = line.Id;
        HeaderText.Text = $"{line.Description} · DDT {document.DocumentCode} a {document.RecipientName}";
        ReturnedAtPicker.SelectedDate = DateTime.Today;
        ShowLine(line);
    }

    private void ShowLine(TransportDocumentLineDto line)
    {
        var outstanding = line.OutstandingQuantity ?? 0;
        BalanceText.Text = $"Inviati {Format(line.Quantity)} {line.Unit} · rientrati {Format(line.ReturnedQuantity ?? 0)}"
            + $" · scartati {Format(line.ScrapQuantity ?? 0)} · ancora presso il terzista {Format(outstanding)} {line.Unit}";
        QuantityBox.Text = outstanding > 0 ? Format(outstanding) : string.Empty;
        EntryPanel.IsEnabled = outstanding > 0;
        ReturnsList.ItemsSource = line.Returns.Select(r => r with { ReturnedAt = r.ReturnedAt.ToLocalTime() }).ToList();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (!NumberInput.TryParseDecimal(string.IsNullOrWhiteSpace(QuantityBox.Text) ? "0" : QuantityBox.Text, out var quantity) || quantity < 0
            || !NumberInput.TryParseDecimal(string.IsNullOrWhiteSpace(ScrapBox.Text) ? "0" : ScrapBox.Text, out var scrap) || scrap < 0)
        {
            ErrorText.Text = "Quantità non valida.";
            return;
        }

        var date = ReturnedAtPicker.SelectedDate ?? DateTime.Today;
        await RunAsync(() => _apiClient.AddSubcontractingReturnAsync(
            _documentId, _lineId, quantity, scrap,
            DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Local).ToUniversalTime(),
            string.IsNullOrWhiteSpace(ReferenceBox.Text) ? null : ReferenceBox.Text.Trim(),
            string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim()));
        if (string.IsNullOrEmpty(ErrorText.Text))
        {
            ReferenceBox.Text = NotesBox.Text = string.Empty;
            ScrapBox.Text = "0";
        }
    }

    private async void DeleteReturn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SubcontractingReturnDto entry }
            && MessageBox.Show("Eliminare questo rientro? La quantità torna a risultare presso il terzista.", "Elimina rientro",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await RunAsync(() => _apiClient.DeleteSubcontractingReturnAsync(_documentId, entry.Id));
        }
    }

    private async Task RunAsync(Func<Task<TransportDocumentDto>> action)
    {
        IsEnabled = false;
        try
        {
            UpdatedDocument = await action();
            ShowLine(UpdatedDocument.Lines.First(line => line.Id == _lineId));
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Format(decimal value) => value.ToString("0.###", Italian);
}
