using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Converts an accepted quote into Draft work orders (one per product line), with an optional
/// due date and destination area applied to all of them.</summary>
public partial class ConvertQuoteWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly QuoteSummaryDto _quote;

    public ConvertQuoteResultDto? Result { get; private set; }

    public ConvertQuoteWindow(ApiClient apiClient, QuoteSummaryDto quote)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _quote = quote;
        HeaderText.Text = $"Converti {quote.Code}";
        SummaryText.Text =
            $"Cliente {quote.CustomerName}. Verrà creata una commessa in bozza per ogni riga collegata a un prodotto; " +
            "le righe libere (trasporto, installazione...) restano solo nel preventivo.";

        Loaded += async (_, _) =>
        {
            try
            {
                AreaCombo.ItemsSource = (await _apiClient.GetAreasAsync()).Where(area => area.IsActive).ToList();
            }
            catch (Exception exception)
            {
                ErrorText.Text = exception.Message;
            }
        };
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        var dueDate = DueDatePicker.SelectedDate is { } date ? DateTime.SpecifyKind(date.Date, DateTimeKind.Utc) : (DateTime?)null;
        var areaId = (AreaCombo.SelectedItem as AreaDto)?.Id;

        ConvertButton.IsEnabled = false;
        try
        {
            Result = await _apiClient.ConvertQuoteAsync(_quote.Id, dueDate, areaId);
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            ConvertButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
