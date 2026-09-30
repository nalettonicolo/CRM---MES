using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

namespace CrmMes.Desktop;

/// <summary>Drawings, wiring diagrams, work instructions and CNC programs of a product. Two uses: from the
/// product card the engineering office uploads new versions and withdraws old ones; at the shop floor terminal
/// the operator only opens the documents of the phase in hand. Files open with the PC's own program, from a
/// private temporary folder; the server already refuses executable files.</summary>
public partial class TechnicalDocumentsWindow : Window
{
    private static readonly string[] Unsafe = [".exe", ".com", ".bat", ".cmd", ".msi", ".scr", ".pif", ".lnk", ".url", ".hta", ".ps1", ".vbs", ".js", ".jar", ".reg", ".html", ".htm", ".svg"];
    private static readonly Dictionary<string, string> Kinds = new()
    {
        ["drawing"] = "Disegno",
        ["schema"] = "Schema elettrico",
        ["instructions"] = "Istruzioni di lavoro",
        ["cnc"] = "Programma CNC",
        ["photo"] = "Foto",
        ["other"] = "Altro",
    };

    private readonly ApiClient _apiClient;
    private readonly Guid _productId;
    private readonly (Guid WorkOrderId, Guid OperationId)? _operation;
    private readonly bool _canEdit;

    private sealed record StepChoice(int? Sequence, string Label);

    /// <summary>Product card: all documents; upload and withdraw for the engineering office.</summary>
    public TechnicalDocumentsWindow(ApiClient apiClient, Guid productId, string productLabel, IEnumerable<(int Sequence, string Name)> steps)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _productId = productId;
        _canEdit = apiClient.CurrentRole is "Admin" or "Management";
        TitleText.Text = $"Documenti tecnici · {productLabel}";
        SubtitleText.Text = "Gli operatori vedono al terminale i documenti della fase che stanno lavorando, più quelli di tutto il prodotto.";
        HistoryBox.Visibility = Visibility.Visible;
        if (_canEdit)
        {
            UploadPanel.Visibility = Visibility.Visible;
            WithdrawButton.Visibility = Visibility.Visible;
            KindBox.ItemsSource = Kinds;
            KindBox.SelectedValue = "drawing";
            StepBox.ItemsSource = new[] { new StepChoice(null, "Tutto il prodotto") }.Concat(steps.OrderBy(s => s.Sequence).Select(s => new StepChoice(s.Sequence, $"{s.Sequence} · {s.Name}"))).ToList();
            StepBox.SelectedIndex = 0;
        }

        Loaded += async (_, _) => await LoadAsync();
    }

    /// <summary>Terminal: the current documents of one phase, to open only.</summary>
    public TechnicalDocumentsWindow(ApiClient apiClient, Guid workOrderId, Guid operationId, string title)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _operation = (workOrderId, operationId);
        TitleText.Text = title;
        SubtitleText.Text = "Doppio clic o Apri per vedere il documento. È sempre l'ultima versione approvata dall'ufficio tecnico.";
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        ErrorText.Text = string.Empty;
        try
        {
            DocumentsList.ItemsSource = _operation is { } op
                ? await _apiClient.GetOperationDocumentsAsync(op.WorkOrderId, op.OperationId)
                : await _apiClient.GetProductDocumentsAsync(_productId, HistoryBox.IsChecked == true);
            if (DocumentsList.Items.Count == 0)
            {
                ErrorText.Text = _operation is null ? "Nessun documento: caricane uno." : "Nessun documento per questa fase.";
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void HistoryBox_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Documento da caricare", Filter = "Documenti (*.pdf;*.dwg;*.dxf;*.step;*.stp;*.png;*.jpg;*.docx;*.xlsx;*.nc;*.tap;*.txt)|*.pdf;*.dwg;*.dxf;*.step;*.stp;*.png;*.jpg;*.jpeg;*.docx;*.xlsx;*.nc;*.tap;*.txt|Tutti i file (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (new FileInfo(dialog.FileName).Length > 20 * 1024 * 1024)
        {
            ErrorText.Text = "Il file supera 20 MB.";
            return;
        }

        UploadButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try
        {
            var step = (StepBox.SelectedItem as StepChoice)?.Sequence;
            var document = await _apiClient.UploadProductDocumentAsync(_productId, dialog.FileName, DocTitleBox.Text, KindBox.SelectedValue as string ?? "drawing", step, VersionNoteBox.Text);
            DocTitleBox.Text = string.Empty;
            VersionNoteBox.Text = string.Empty;
            await LoadAsync();
            ErrorText.Text = $"Caricato \"{document.Title}\" (versione {document.Version}).";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            UploadButton.IsEnabled = true;
        }
    }

    private async void DocumentsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await OpenSelectedAsync();

    private async void Open_Click(object sender, RoutedEventArgs e) => await OpenSelectedAsync();

    private async Task OpenSelectedAsync()
    {
        if (DocumentsList.SelectedItem is not TechnicalDocumentDto document)
        {
            ErrorText.Text = "Seleziona un documento.";
            return;
        }

        var safeName = string.Concat(Path.GetFileName(document.FileName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (Unsafe.Contains(Path.GetExtension(safeName).ToLowerInvariant()))
        {
            ErrorText.Text = "Questo tipo di file non si apre dal gestionale.";
            return;
        }

        ErrorText.Text = string.Empty;
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "NicoloMES", "documenti", document.Id.ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, safeName.Length == 0 ? "documento" : safeName);
            await File.WriteAllBytesAsync(path, await _apiClient.DownloadTechnicalDocumentAsync(document.Id));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            ErrorText.Text = exception is System.ComponentModel.Win32Exception
                ? "Nessun programma su questo PC apre questo tipo di file."
                : exception.Message;
        }
    }

    private async void Withdraw_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentsList.SelectedItem is not TechnicalDocumentDto { IsCurrent: true } document)
        {
            ErrorText.Text = "Seleziona un documento attuale.";
            return;
        }

        if (MessageBox.Show(this, $"Ritirare \"{document.Title}\"? Non sarà più visibile al terminale, ma resta nello storico.", "Ritira documento",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.WithdrawTechnicalDocumentAsync(document.Id);
            await LoadAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
