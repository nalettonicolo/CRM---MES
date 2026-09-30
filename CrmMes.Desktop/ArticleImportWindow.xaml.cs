using System.IO;
using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Article import in two steps: the file is checked first (what would be created, updated, skipped,
/// every problem with its row), and imported only after "Importa". Rows with errors are skipped, the rest
/// goes in.</summary>
public partial class ArticleImportWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly string _filePath;

    public bool Imported { get; private set; }

    public ArticleImportWindow(ApiClient apiClient, string filePath)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _filePath = filePath;
        FileText.Text = Path.GetFileName(filePath);
        Loaded += async (_, _) => await PreviewAsync();
    }

    private async Task PreviewAsync()
    {
        ImportButton.IsEnabled = false;
        SummaryText.Text = "Controllo del file in corso...";
        try
        {
            var result = await _apiClient.ImportArticlesAsync(_filePath, preview: true, update: UpdateBox.IsChecked == true);
            Show(result);
            ImportButton.IsEnabled = result.Created + result.Updated > 0;
        }
        catch (InvalidOperationException exception)
        {
            SummaryText.Text = exception.Message;
        }
    }

    private void Show(ArticleImportDto result)
    {
        var columns = result.Columns.Count == 0 ? string.Empty
            : "Colonne riconosciute: " + string.Join(", ", result.Columns.Values) + "." + Environment.NewLine;
        SummaryText.Text = columns + (result.Preview
            ? $"{result.Rows} righe: {result.Created} articoli nuovi, {result.Updated} da aggiornare, {result.Skipped} già presenti" +
              (result.Unchanged > 0 ? $", {result.Unchanged} invariati" : string.Empty) +
              $". {result.ErrorCount} righe con errori (saltate), {result.WarningCount} avvisi."
            : $"Importazione completata: {result.Created} articoli nuovi, {result.Updated} aggiornati, {result.Skipped} già presenti lasciati com'erano. {result.ErrorCount} righe saltate per errori.");
        SampleList.ItemsSource = result.Samples;
        ErrorList.ItemsSource = result.Errors;
        WarningList.ItemsSource = result.Warnings;
        ErrorsTab.Header = $"Errori ({result.ErrorCount})";
        WarningsTab.Header = $"Avvisi ({result.WarningCount})";
    }

    private async void UpdateBox_Click(object sender, RoutedEventArgs e) => await PreviewAsync();

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = false;
        try
        {
            var result = await _apiClient.ImportArticlesAsync(_filePath, preview: false, update: UpdateBox.IsChecked == true);
            Show(result);
            Imported = true;
            ImportButton.Visibility = Visibility.Collapsed;
        }
        catch (InvalidOperationException exception)
        {
            SummaryText.Text = exception.Message;
            ImportButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
