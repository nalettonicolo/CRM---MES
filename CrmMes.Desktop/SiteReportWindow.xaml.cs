using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrmMes.Desktop;

/// <summary>Rapportino di cantiere from the office: writes and edits drafts, shows signed reports with
/// the customer's signature and prints them. The signature itself is collected on a phone or tablet
/// through the technicians' web page, where the customer can sign with a finger.</summary>
public partial class SiteReportWindow : Window
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private static readonly WorkCenterDto NoWorkCenter = new(Guid.Empty, string.Empty, "— nessuno —", null, 0, true, null, null);

    private readonly ApiClient _apiClient;
    private readonly Guid? _workOrderId;
    private readonly ObservableCollection<SiteReportHoursDto> _hours = [];
    private readonly ObservableCollection<SiteReportMaterialDto> _materials = [];
    private Guid? _reportId;
    private SiteReportDto? _report;

    /// <summary>True when something was saved, so the list behind can reload.</summary>
    public bool Changed { get; private set; }

    public SiteReportWindow(ApiClient apiClient, Guid? reportId = null, Guid? workOrderId = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _reportId = reportId;
        _workOrderId = workOrderId;
        HoursList.ItemsSource = _hours;
        MaterialsList.ItemsSource = _materials;
        DatePicker.SelectedDate = DateTime.Today;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var workCenters = await _apiClient.GetWorkCentersAsync();
            WorkCenterCombo.ItemsSource = new[] { NoWorkCenter }.Concat(workCenters).ToList();
            WorkCenterCombo.SelectedIndex = 0;

            if (_reportId is { } id)
            {
                Show(await _apiClient.GetSiteReportAsync(id));
                return;
            }

            var orders = await _apiClient.GetOpenSiteWorkOrdersAsync();
            WorkOrderCombo.ItemsSource = orders;
            WorkOrderCombo.SelectedItem = orders.FirstOrDefault(o => o.Id == _workOrderId);
            TechnicianBox.Text = string.Empty;
            ApplyState();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Show(SiteReportDto report)
    {
        _report = report;
        _reportId = report.Id;
        WorkOrderCombo.ItemsSource = new[] { new SiteWorkOrderDto(report.WorkOrderId, report.WorkOrderCode, report.ProductName, report.CustomerName, report.SiteAddress, "", null) };
        WorkOrderCombo.SelectedIndex = 0;
        DatePicker.SelectedDate = report.WorkDate.Date;
        AddressBox.Text = report.SiteAddress ?? string.Empty;
        DescriptionBox.Text = report.Description;
        NotesBox.Text = report.Notes ?? string.Empty;
        _hours.Clear();
        report.Hours.ForEach(_hours.Add);
        _materials.Clear();
        report.Materials.ForEach(_materials.Add);
        ApplyState();
    }

    private void ApplyState()
    {
        var signed = _report?.IsSigned == true;
        HeaderText.Text = _report?.Code ?? "Nuovo rapportino";
        SubHeaderText.Text = _report is null
            ? "Salva la bozza, poi fai firmare il cliente dalla pagina web dei tecnici."
            : signed
                ? $"Firmato da {_report.SignedByName} il {_report.SignedAt?.ToLocalTime():dd/MM/yyyy HH:mm}: ore e materiali sono nei costi della commessa."
                : $"Bozza di {_report.CreatedBy} · {_report.WorkOrderCode} · {_report.CustomerName}";
        StatusText.Text = signed ? "FIRMATO" : "BOZZA";
        StatusText.Foreground = (Brush)new StatusToBrushConverter().Convert(signed ? "Completed" : "Draft", typeof(Brush), "Foreground", Italian);

        foreach (var control in new FrameworkElement[] { WorkOrderCombo, DatePicker, AddressBox, DescriptionBox, NotesBox })
        {
            control.IsEnabled = !signed;
        }

        WorkOrderCombo.IsEnabled = _report is null;
        HoursEntry.Visibility = MaterialEntry.Visibility = signed ? Visibility.Collapsed : Visibility.Visible;
        HoursList.IsEnabled = MaterialsList.IsEnabled = !signed;
        SaveButton.Visibility = signed ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.Visibility = !signed && _report is not null ? Visibility.Visible : Visibility.Collapsed;
        WebButton.Visibility = signed ? Visibility.Collapsed : Visibility.Visible;
        PdfButton.IsEnabled = _report is not null;

        SignaturePanel.Visibility = signed ? Visibility.Visible : Visibility.Collapsed;
        if (signed)
        {
            SignedByText.Text = $"Firma del cliente: {_report!.SignedByName}";
            SignatureImage.Source = DecodeSignature(_report.SignatureImage);
        }
    }

    private static BitmapImage? DecodeSignature(string? dataUrl)
    {
        const string header = "data:image/png;base64,";
        if (dataUrl is null || !dataUrl.StartsWith(header, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(Convert.FromBase64String(dataUrl[header.Length..]));
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null; // not a readable image: the report still shows who signed and when
        }
    }

    private void WorkOrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_report is null && WorkOrderCombo.SelectedItem is SiteWorkOrderDto order && string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            AddressBox.Text = order.CustomerAddress ?? string.Empty;
        }
    }

    private void AddHours_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(TechnicianBox.Text))
        {
            ErrorText.Text = "Indica il tecnico.";
            return;
        }

        if (!LaborEntriesWindow.TryParseDuration(HoursBox.Text, out var minutes))
        {
            ErrorText.Text = "Ore non valide (es. 1:30 o 1,5).";
            return;
        }

        var workCenter = WorkCenterCombo.SelectedItem as WorkCenterDto;
        var hasWorkCenter = workCenter is { Id: var id } && id != Guid.Empty;
        _hours.Add(new SiteReportHoursDto(TechnicianBox.Text.Trim(), hasWorkCenter ? workCenter!.Id : null, hasWorkCenter ? workCenter!.Name : null, minutes));
        TechnicianBox.Text = HoursBox.Text = string.Empty;
        TechnicianBox.Focus();
    }

    private void RemoveHours_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SiteReportHoursDto row })
        {
            _hours.Remove(row);
        }
    }

    private async void MaterialCodeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var code = MaterialCodeBox.Text.Trim();
        if (code.Length == 0)
        {
            return;
        }

        try
        {
            var match = (await _apiClient.SearchMaterialsAsync(code)).FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));
            if (match is not null && MaterialCodeBox.Text.Trim().Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                MaterialCodeBox.Text = match.Code;
                MaterialDescriptionBox.Text = match.Name;
                MaterialUnitBox.Text = match.Unit;
            }
        }
        catch
        {
            // Offline or not found: free text is accepted as well.
        }
    }

    private void AddMaterial_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var code = string.IsNullOrWhiteSpace(MaterialCodeBox.Text) ? null : MaterialCodeBox.Text.Trim();
        if (code is null && string.IsNullOrWhiteSpace(MaterialDescriptionBox.Text))
        {
            ErrorText.Text = "Indica codice o descrizione del materiale.";
            return;
        }

        if (!NumberInput.TryParseDecimal(MaterialQuantityBox.Text, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Quantità non valida.";
            return;
        }

        _materials.Add(new SiteReportMaterialDto(code, MaterialDescriptionBox.Text.Trim(), quantity,
            string.IsNullOrWhiteSpace(MaterialUnitBox.Text) ? "pz" : MaterialUnitBox.Text.Trim()));
        MaterialCodeBox.Text = MaterialDescriptionBox.Text = MaterialQuantityBox.Text = string.Empty;
        MaterialUnitBox.Text = "pz";
        MaterialCodeBox.Focus();
    }

    private void RemoveMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SiteReportMaterialDto row })
        {
            _materials.Remove(row);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (WorkOrderCombo.SelectedItem is not SiteWorkOrderDto order)
        {
            ErrorText.Text = "Scegli la commessa.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            var saved = await _apiClient.SaveSiteReportAsync(_reportId, new SaveSiteReportDto(
                order.Id,
                DateTime.SpecifyKind((DatePicker.SelectedDate ?? DateTime.Today).Date, DateTimeKind.Utc),
                string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                DescriptionBox.Text.Trim(),
                string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
                _hours.Select(h => new SiteReportHoursRequestDto(h.TechnicianName, h.WorkCenterId, h.Minutes)).ToList(),
                _materials.Select(m => new SiteReportMaterialRequestDto(m.MaterialCode, m.Description, m.Quantity, m.Unit)).ToList()));
            Changed = true;
            Show(saved);
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

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_reportId is not { } id || MessageBox.Show("Eliminare questa bozza?", "Elimina bozza", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeleteSiteReportAsync(id);
            Changed = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Pdf_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"{_report.Code}.pdf", Filter = "File PDF (*.pdf)|*.pdf" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ListExporter.ExportSiteReport(_report, _apiClient.CompanyProfile, dialog.FileName);
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Web_Click(object sender, RoutedEventArgs e)
    {
        var url = new Uri(_apiClient.BaseAddress, "tecnici/").ToString();
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"Apri {url} sul tablet del tecnico. ({exception.Message})";
        }
    }
}
