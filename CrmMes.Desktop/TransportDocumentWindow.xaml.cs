using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Creates and edits a DDT while it's a draft; once issued the same window shows it read-only,
/// prints it, cancels it and — for conto lavorazione — records what comes back from the subcontractor.</summary>
public partial class TransportDocumentWindow : Window
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private static readonly CustomerDto NoCustomer = new(Guid.Empty, string.Empty, "— nessuno —", null, null, null, null, null, true);
    private static readonly SupplierDto NoSupplier = new(Guid.Empty, "— nessuno —", string.Empty, null, null, null, true);
    private static readonly CarrierDto NoCarrier = new(Guid.Empty, "— nessuno —", string.Empty, null, null, true);

    private readonly ApiClient _apiClient;
    private readonly ObservableCollection<TransportLineRow> _lines = [];
    private Guid? _documentId;
    private TransportDocumentDto? _document;
    private bool _loading = true;
    private Guid? _materialId;
    private readonly string? _initialReason;

    /// <summary>True when something was saved, so the list behind can reload.</summary>
    public bool Changed { get; private set; }

    public TransportDocumentWindow(ApiClient apiClient, Guid? documentId = null, string? initialReason = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _documentId = documentId;
        _initialReason = initialReason;
        LinesList.ItemsSource = _lines;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var reasons = _apiClient.GetTransportReasonsAsync();
            var customers = _apiClient.GetCustomersAsync();
            var suppliers = _apiClient.GetSuppliersAsync();
            var carriers = _apiClient.GetCarriersAsync();
            await Task.WhenAll(reasons, customers, suppliers, carriers);

            ReasonCombo.ItemsSource = reasons.Result;
            CustomerCombo.ItemsSource = new[] { NoCustomer }.Concat(customers.Result).ToList();
            SupplierCombo.ItemsSource = new[] { NoSupplier }.Concat(suppliers.Result.Where(s => s.IsActive)).ToList();
            CarrierCombo.ItemsSource = new[] { NoCarrier }.Concat(carriers.Result).ToList();

            if (_documentId is { } id)
            {
                Show(await _apiClient.GetTransportDocumentAsync(id));
            }
            else
            {
                ReasonCombo.SelectedItem = reasons.Result.FirstOrDefault(r => r.Key == _initialReason) ?? reasons.Result.FirstOrDefault();
                CustomerCombo.SelectedIndex = 0;
                SupplierCombo.SelectedIndex = 0;
                CarrierCombo.SelectedIndex = 0;
                ApplyState();
            }
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Show(TransportDocumentDto document)
    {
        var wasLoading = _loading;
        _loading = true;
        _document = document;
        _documentId = document.Id;

        ReasonCombo.SelectedItem = ((IEnumerable<TransportReasonDto>)ReasonCombo.ItemsSource).FirstOrDefault(r => r.Key == document.Reason);
        ReasonDetailBox.Text = document.ReasonDetail ?? string.Empty;
        CustomerCombo.SelectedItem = ((IEnumerable<CustomerDto>)CustomerCombo.ItemsSource).FirstOrDefault(c => c.Id == document.CustomerId) ?? NoCustomer;
        SupplierCombo.SelectedItem = ((IEnumerable<SupplierDto>)SupplierCombo.ItemsSource).FirstOrDefault(s => s.Id == document.SupplierId) ?? NoSupplier;
        RecipientNameBox.Text = document.RecipientName;
        RecipientAddressBox.Text = document.RecipientAddress ?? string.Empty;
        RecipientVatBox.Text = document.RecipientVatNumber ?? string.Empty;
        DestinationBox.Text = document.DestinationAddress ?? string.Empty;
        SelectByTag(TransportByCombo, document.TransportBy);
        CarrierCombo.SelectedItem = ((IEnumerable<CarrierDto>)CarrierCombo.ItemsSource).FirstOrDefault(c => c.Id == document.CarrierId) ?? NoCarrier;
        SelectByTag(PortCombo, document.Port ?? string.Empty);
        AppearanceBox.Text = document.GoodsAppearance ?? string.Empty;
        PackagesBox.Text = document.Packages?.ToString(Italian) ?? string.Empty;
        WeightBox.Text = document.GrossWeightKg?.ToString("0.###", Italian) ?? string.Empty;
        ExpectedReturnPicker.SelectedDate = document.ExpectedReturnAt?.ToLocalTime().Date;
        NotesBox.Text = document.Notes ?? string.Empty;

        _lines.Clear();
        foreach (var line in document.Lines)
        {
            _lines.Add(TransportLineRow.From(line, document));
        }

        ApplyState();
        _loading = wasLoading;
    }

    /// <summary>Draft: everything editable. Issued: read-only, print and cancel; conto lavorazione also
    /// shows what came back. Cancelled: read-only, print only.</summary>
    private void ApplyState()
    {
        var status = _document?.Status ?? "Draft";
        var isDraft = status == "Draft";
        var subcontracting = SelectedReasonKey == "Subcontracting";

        HeaderText.Text = _document is null ? "Nuovo DDT" : $"DDT {_document.DocumentCode}";
        StatusText.Text = TransportDocumentDto.StatusText(status).ToUpperInvariant();
        StatusText.Foreground = (Brush)new StatusToBrushConverter().Convert(status, typeof(Brush), "Foreground", Italian);
        SubHeaderText.Text = _document switch
        {
            null => "Il numero si assegna all'emissione, in ordine progressivo nell'anno.",
            { Status: "Cancelled" } => $"Annullato il {_document.CancelledAt?.ToLocalTime():dd/MM/yyyy}: {_document.CancellationReason}",
            { Status: "Issued" } => $"Emesso il {_document.IssuedAt?.ToLocalTime():dd/MM/yyyy HH:mm} da {_document.IssuedBy}"
                + (_document.WorkOrderCode is null ? string.Empty : $" · commessa {_document.WorkOrderCode}"),
            _ => "Bozza: modificabile finché non viene emessa." + (_document.WorkOrderCode is null ? string.Empty : $" Commessa {_document.WorkOrderCode}.")
        };

        HeaderPanel.IsEnabled = isDraft;
        LineEntryPanel.Visibility = isDraft ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Visibility = isDraft ? Visibility.Visible : Visibility.Collapsed;
        IssueButton.Visibility = isDraft ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Visibility = isDraft && _document is not null ? Visibility.Visible : Visibility.Collapsed;
        CancelDocumentButton.Visibility = status == "Issued" ? Visibility.Visible : Visibility.Collapsed;
        PdfButton.Content = isDraft ? "Anteprima" : "PDF";
        PdfButton.Width = isDraft ? 100 : 80;

        ReasonDetailBox.Visibility = SelectedReasonKey == "Other" ? Visibility.Visible : Visibility.Collapsed;
        ExpectedReturnPanel.Visibility = subcontracting ? Visibility.Visible : Visibility.Collapsed;
        SupplierLabel.Text = subcontracting ? "Terzista *" : "Fornitore / terzista";

        // Returned/outstanding columns only mean something for goods sent in conto lavorazione.
        var showReturns = subcontracting && !isDraft;
        ReturnedColumn.Width = showReturns ? 90 : 0;
        OutstandingColumn.Width = showReturns ? 80 : 0;
    }

    private string? SelectedReasonKey => (ReasonCombo.SelectedItem as TransportReasonDto)?.Key;

    private void ReasonCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyState();

    private void CustomerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CustomerCombo.SelectedItem is not CustomerDto { Id: var id } customer || id == Guid.Empty)
        {
            return;
        }

        SupplierCombo.SelectedItem = NoSupplier;
        RecipientNameBox.Text = customer.Name;
        RecipientAddressBox.Text = customer.Address ?? string.Empty;
        RecipientVatBox.Text = customer.VatNumber ?? string.Empty;
    }

    private void SupplierCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SupplierCombo.SelectedItem is not SupplierDto { Id: var id } supplier || id == Guid.Empty)
        {
            return;
        }

        CustomerCombo.SelectedItem = NoCustomer;
        RecipientNameBox.Text = supplier.Name;
        RecipientAddressBox.Text = string.Empty;
        RecipientVatBox.Text = string.Empty;
    }

    private void TransportByCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CarrierCombo is null)
        {
            return; // fired while the XAML is still being built
        }

        var byCarrier = TagOf(TransportByCombo) == "Carrier";
        CarrierCombo.IsEnabled = byCarrier;
        if (!byCarrier)
        {
            CarrierCombo.SelectedItem = NoCarrier;
        }
    }

    /// <summary>A code typed in the line strip that matches a material fills description and unit.</summary>
    private async void LineCodeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _materialId = null;
        var code = LineCodeBox.Text.Trim();
        if (code.Length == 0)
        {
            return;
        }

        try
        {
            var match = (await _apiClient.SearchMaterialsAsync(code))
                .FirstOrDefault(material => string.Equals(material.Code, code, StringComparison.OrdinalIgnoreCase));
            if (match is not null && LineCodeBox.Text.Trim().Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                _materialId = match.Id;
                LineCodeBox.Text = match.Code;
                LineDescriptionBox.Text = match.Name;
                LineUnitBox.Text = match.Unit;
            }
        }
        catch
        {
            // Offline or not found: the line stays free text, which is valid.
        }
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(LineDescriptionBox.Text))
        {
            ErrorText.Text = "Scrivi la descrizione dei beni.";
            LineDescriptionBox.Focus();
            return;
        }

        if (!NumberInput.TryParseDecimal(LineQuantityBox.Text, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Quantità non valida.";
            LineQuantityBox.Focus();
            return;
        }

        _lines.Add(new TransportLineRow
        {
            MaterialId = _materialId,
            Code = NullIfEmpty(LineCodeBox.Text),
            Description = LineDescriptionBox.Text.Trim(),
            Quantity = quantity,
            Unit = NullIfEmpty(LineUnitBox.Text) ?? "pz",
            LotNumber = NullIfEmpty(LineLotBox.Text),
            CanRemove = true
        });
        _materialId = null;
        LineCodeBox.Text = LineDescriptionBox.Text = LineLotBox.Text = string.Empty;
        LineQuantityBox.Text = "1";
        LineUnitBox.Text = "pz";
        LineCodeBox.Focus();
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TransportLineRow row })
        {
            _lines.Remove(row);
        }
    }

    private SaveTransportDocumentDto? BuildRequest()
    {
        ErrorText.Text = string.Empty;
        if (SelectedReasonKey is not { } reason)
        {
            ErrorText.Text = "Scegli la causale.";
            return null;
        }

        int? packages = null;
        if (!string.IsNullOrWhiteSpace(PackagesBox.Text))
        {
            if (!int.TryParse(PackagesBox.Text.Trim(), NumberStyles.Integer, Italian, out var parsedPackages) || parsedPackages < 0)
            {
                ErrorText.Text = "Numero di colli non valido.";
                return null;
            }

            packages = parsedPackages;
        }

        decimal? weight = null;
        if (!string.IsNullOrWhiteSpace(WeightBox.Text))
        {
            if (!NumberInput.TryParseDecimal(WeightBox.Text, out var parsedWeight) || parsedWeight < 0)
            {
                ErrorText.Text = "Peso non valido.";
                return null;
            }

            weight = parsedWeight;
        }

        if (_lines.Count == 0)
        {
            ErrorText.Text = "Aggiungi almeno una riga.";
            return null;
        }

        var customer = CustomerCombo.SelectedItem as CustomerDto;
        var supplier = SupplierCombo.SelectedItem as SupplierDto;
        var carrier = CarrierCombo.SelectedItem as CarrierDto;
        var expectedReturn = ExpectedReturnPicker.SelectedDate;
        return new SaveTransportDocumentDto(
            reason,
            NullIfEmpty(ReasonDetailBox.Text),
            customer is { Id: var customerId } && customerId != Guid.Empty ? customerId : null,
            supplier is { Id: var supplierId } && supplierId != Guid.Empty ? supplierId : null,
            NullIfEmpty(RecipientNameBox.Text),
            NullIfEmpty(RecipientAddressBox.Text),
            NullIfEmpty(RecipientVatBox.Text),
            NullIfEmpty(DestinationBox.Text),
            TagOf(TransportByCombo) ?? "Sender",
            carrier is { Id: var carrierId } && carrierId != Guid.Empty ? carrierId : null,
            NullIfEmpty(TagOf(PortCombo) ?? string.Empty),
            NullIfEmpty(AppearanceBox.Text),
            packages,
            weight,
            null,
            expectedReturn.HasValue ? DateTime.SpecifyKind(expectedReturn.Value.Date, DateTimeKind.Local).ToUniversalTime() : null,
            _document?.WorkOrderId,
            NullIfEmpty(NotesBox.Text),
            _lines.Select(line => new SaveTransportDocumentLineDto(
                line.MaterialId, line.ProductId, line.Code, line.Description, line.Quantity, line.Unit, line.LotNumber, line.Notes)).ToList());
    }

    private async Task<bool> SaveDraftAsync()
    {
        var request = BuildRequest();
        if (request is null)
        {
            return false;
        }

        var saved = _documentId is { } id
            ? await _apiClient.UpdateTransportDocumentAsync(id, request)
            : await _apiClient.CreateTransportDocumentAsync(request);
        Changed = true;
        Show(saved);
        return true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            if (await SaveDraftAsync())
            {
                ErrorText.Text = string.Empty;
            }
        });
    }

    private async void Issue_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            if (!await SaveDraftAsync())
            {
                return;
            }

            var confirm = MessageBox.Show(
                "Emettere il DDT? Riceverà il numero progressivo e non sarà più modificabile (solo annullabile).",
                "Emetti DDT", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            Show(await _apiClient.IssueTransportDocumentAsync(_documentId!.Value));
            Changed = true;
            if (MessageBox.Show($"DDT {_document!.DocumentCode} emesso. Vuoi salvarlo in PDF per stamparlo?", "DDT emesso",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                ExportPdf();
            }
        });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_documentId is not { } id
            || MessageBox.Show("Eliminare questa bozza?", "Elimina bozza", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _apiClient.DeleteTransportDocumentAsync(id);
            Changed = true;
            Close();
        });
    }

    private async void CancelDocument_Click(object sender, RoutedEventArgs e)
    {
        if (_documentId is not { } id)
        {
            return;
        }

        var prompt = new TextPromptWindow("Annulla DDT",
            "Il DDT resta in archivio con il suo numero, segnato come annullato. Motivo dell'annullamento:") { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            Show(await _apiClient.CancelTransportDocumentAsync(id, prompt.Value));
            Changed = true;
        });
    }

    private void Returns_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TransportLineRow row } || _document is null || row.Id is not { } lineId)
        {
            return;
        }

        var line = _document.Lines.First(l => l.Id == lineId);
        var window = new SubcontractingReturnWindow(_apiClient, _document, line) { Owner = this };
        window.ShowDialog();
        if (window.UpdatedDocument is { } updated)
        {
            Changed = true;
            Show(updated);
        }
    }

    private void Pdf_Click(object sender, RoutedEventArgs e) => ExportPdf();

    private void ExportPdf()
    {
        // A draft is previewed from the fields on screen, saved or not; an issued DDT prints as issued.
        var document = _document is { IsDraft: false } ? _document : PreviewDocument();
        if (document is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = document.IsDraft ? "DDT-bozza.pdf" : $"DDT-{document.Number}-{document.Year}.pdf",
            Filter = "File PDF (*.pdf)|*.pdf"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ListExporter.ExportTransportDocument(document, _apiClient.CompanyProfile, dialog.FileName);
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>A never-saved document shown only for the preview of a brand new DDT.</summary>
    private TransportDocumentDto? PreviewDocument()
    {
        var request = BuildRequest();
        if (request is null)
        {
            return null;
        }

        var reason = (TransportReasonDto)ReasonCombo.SelectedItem;
        return new TransportDocumentDto(
            _document?.Id ?? Guid.Empty, "Bozza", null, null, "Draft", reason.Key, request.ReasonDetail,
            reason.Key == "Other" && request.ReasonDetail is not null ? request.ReasonDetail : reason.Label,
            request.CustomerId, request.SupplierId, request.RecipientName ?? string.Empty, request.RecipientAddress,
            request.RecipientVatNumber, request.DestinationAddress, request.TransportBy, request.CarrierId,
            (CarrierCombo.SelectedItem as CarrierDto) is { Id: var carrierId } carrier && carrierId != Guid.Empty ? carrier.Name : null,
            request.Port, request.GoodsAppearance, request.Packages, request.GrossWeightKg, null, request.ExpectedReturnAt,
            request.WorkOrderId, _document?.WorkOrderCode, request.Notes, null, DateTime.UtcNow, null, null, null, null,
            request.Lines.Select((line, index) => new TransportDocumentLineDto(
                Guid.Empty, index + 1, line.MaterialId, line.ProductId, line.Code, line.Description ?? string.Empty, line.Quantity,
                line.Unit ?? "pz", line.LotNumber, line.Notes, null, null, null, [])).ToList());
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsEnabled = false;
        try
        {
            await action();
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

    private static void SelectByTag(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (item.Tag as string) == tag) ?? combo.Items[0];

    private static string? TagOf(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>A line as shown in the DDT window: typed in (draft) or read back from the server.</summary>
public sealed class TransportLineRow
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public Guid? Id { get; init; }
    public Guid? MaterialId { get; init; }
    public Guid? ProductId { get; init; }
    public string? Code { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = "pz";
    public string? LotNumber { get; init; }
    public string? Notes { get; init; }
    public decimal? Returned { get; init; }
    public decimal? Scrap { get; init; }
    public decimal? Outstanding { get; init; }
    public bool CanRemove { get; init; }
    public bool CanReturn { get; init; }

    public string ReturnedText => Returned is null ? string.Empty
        : Scrap > 0 ? $"{Returned.Value.ToString("0.###", Italian)} + {Scrap.Value.ToString("0.###", Italian)} sc." : Returned.Value.ToString("0.###", Italian);

    public string OutstandingText => Outstanding?.ToString("0.###", Italian) ?? string.Empty;

    public static TransportLineRow From(TransportDocumentLineDto line, TransportDocumentDto document) => new()
    {
        Id = line.Id,
        MaterialId = line.MaterialId,
        ProductId = line.ProductId,
        Code = line.Code,
        Description = line.Description,
        Quantity = line.Quantity,
        Unit = line.Unit,
        LotNumber = line.LotNumber,
        Notes = line.Notes,
        Returned = line.ReturnedQuantity,
        Scrap = line.ScrapQuantity,
        Outstanding = line.OutstandingQuantity,
        CanRemove = document.IsDraft,
        CanReturn = document.IsSubcontracting && document.Status == "Issued"
    };
}
