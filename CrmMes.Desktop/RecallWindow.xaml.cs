using System.Diagnostics;
using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Guided recall: from a material lot or a product lot to the jobs, shipments and customers
/// involved, with a PDF report to act on.</summary>
public partial class RecallWindow : Window
{
    private readonly ApiClient _apiClient;
    private RecallDto? _recall;

    private RecallWindow(ApiClient apiClient, Func<Task<RecallDto>> load)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) =>
        {
            try
            {
                _recall = await load();
                SubjectText.Text = _recall.Subject;
                WarningsList.ItemsSource = _recall.Warnings;
                CustomersText.Text = _recall.Customers.Count == 0 ? "Nessuna consegna registrata." : string.Join(", ", _recall.Customers);
                WorkOrdersList.ItemsSource = _recall.WorkOrders;
                ShipmentsList.ItemsSource = _recall.Shipments.Select(s => s with { IssuedAt = s.IssuedAt?.ToLocalTime() }).ToList();
                PdfButton.IsEnabled = true;
            }
            catch (Exception exception)
            {
                SubjectText.Text = "Richiamo non disponibile";
                ErrorText.Text = exception.Message;
            }
        };
    }

    public static RecallWindow ForMaterialLot(ApiClient apiClient, Guid lotId) =>
        new(apiClient, () => apiClient.GetRecallFromMaterialLotAsync(lotId));

    public static RecallWindow ForProductLot(ApiClient apiClient, string lotNumber) =>
        new(apiClient, () => apiClient.GetRecallFromProductLotAsync(lotNumber));

    private void Pdf_Click(object sender, RoutedEventArgs e)
    {
        if (_recall is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "richiamo.pdf", Filter = "File PDF (*.pdf)|*.pdf" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ListExporter.ExportRecall(_recall, _apiClient.CompanyProfile, dialog.FileName);
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }
}
