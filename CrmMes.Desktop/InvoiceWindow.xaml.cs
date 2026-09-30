using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Electronic invoice: edits a draft (prices, VAT, payment), issues it and downloads the
/// FatturaPA XML to upload to the Exchange System (or give to the accountant), plus a courtesy PDF copy.</summary>
public partial class InvoiceWindow : Window
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private readonly ApiClient _apiClient;
    private readonly ObservableCollection<InvoiceLineRow> _lines = [];
    private Guid? _invoiceId;
    private InvoiceDto? _invoice;

    public bool Changed { get; private set; }

    public InvoiceWindow(ApiClient apiClient, Guid? invoiceId = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _invoiceId = invoiceId;
        LinesList.ItemsSource = _lines;
        _lines.CollectionChanged += (_, _) => UpdateTotals();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            CustomerCombo.ItemsSource = await _apiClient.GetCustomersAsync();
            if (_invoiceId is { } id)
            {
                Show(await _apiClient.GetInvoiceAsync(id));
            }
            else
            {
                _lines.Add(NewRow(true));
                ApplyState();
            }
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private InvoiceLineRow NewRow(bool editable) => new(UpdateTotals) { IsEditable = editable };

    private void Show(InvoiceDto invoice)
    {
        _invoice = invoice;
        _invoiceId = invoice.Id;
        CustomerCombo.SelectedItem = ((IEnumerable<CustomerDto>)CustomerCombo.ItemsSource).FirstOrDefault(c => c.Id == invoice.CustomerId);
        PaymentCombo.SelectedValue = invoice.PaymentMethod;
        DueDatePicker.SelectedDate = invoice.PaymentDueDate?.Date;
        NotesBox.Text = invoice.Notes ?? string.Empty;
        _lines.Clear();
        foreach (var line in invoice.Lines)
        {
            _lines.Add(new InvoiceLineRow(UpdateTotals, line) { IsEditable = invoice.IsDraft });
        }

        WarningsList.ItemsSource = invoice.Warnings;
        ApplyState();
    }

    private void ApplyState()
    {
        var draft = _invoice?.IsDraft ?? true;
        HeaderText.Text = _invoice is null ? "Nuova fattura" : $"Fattura {_invoice.Code}";
        SubHeaderText.Text = _invoice switch
        {
            null => "Fattura immediata (TD01). Per fatturare DDT usa \"Fattura da DDT\" nell'elenco.",
            { IsDraft: false } => $"Emessa il {_invoice.IssueDate:dd/MM/yyyy} da {_invoice.IssuedBy} · {(_invoice.DocumentType == "TD24" ? "differita" : "immediata")}"
                + (_invoice.TransportDocuments.Count > 0 ? $" · DDT {string.Join(", ", _invoice.TransportDocuments.Select(d => d.DocumentCode))}" : string.Empty),
            _ => $"Bozza {(_invoice.DocumentType == "TD24" ? "di fattura differita" : "di fattura immediata")}"
                + (_invoice.TransportDocuments.Count > 0 ? $" · DDT {string.Join(", ", _invoice.TransportDocuments.Select(d => d.DocumentCode))}" : string.Empty)
        };
        StatusText.Text = draft ? "BOZZA" : "EMESSA";
        StatusText.Foreground = (Brush)new StatusToBrushConverter().Convert(draft ? "Draft" : "Completed", typeof(Brush), "Foreground", Italian);
        HeaderPanel.IsEnabled = draft;
        CustomerCombo.IsEnabled = _invoice is null;
        LineTools.Visibility = draft ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Visibility = IssueButton.Visibility = draft ? Visibility.Visible : Visibility.Collapsed;
        DeleteButton.Visibility = draft && _invoice is not null ? Visibility.Visible : Visibility.Collapsed;
        XmlButton.IsEnabled = !draft;
        XmlButton.ToolTip = draft ? "Disponibile dopo l'emissione" : "File da caricare sul portale Fatture e Corrispettivi o da dare al commercialista";
        PdfButton.IsEnabled = _invoice is not null;
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        if (TotalsText is null)
        {
            return;
        }

        var valid = _lines.Where(l => l.TryRead(out _)).ToList();
        var taxable = valid.Sum(l => l.LineTotal);
        var tax = valid.GroupBy(l => l.VatRate).Sum(g => Math.Round(g.Sum(l => l.LineTotal) * g.Key / 100m, 2, MidpointRounding.AwayFromZero));
        TotalsText.Text = $"Imponibile {taxable.ToString("N2", Italian)} €  ·  IVA {tax.ToString("N2", Italian)} €  ·  Totale {(taxable + tax).ToString("N2", Italian)} €";
    }

    private void AddLine_Click(object sender, RoutedEventArgs e) => _lines.Add(NewRow(true));

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InvoiceLineRow row })
        {
            _lines.Remove(row);
        }
    }

    private SaveInvoiceDto? BuildRequest()
    {
        ErrorText.Text = string.Empty;
        if (CustomerCombo.SelectedItem is not CustomerDto customer)
        {
            ErrorText.Text = "Scegli il cliente.";
            return null;
        }

        var lines = new List<InvoiceLineRequestDto>();
        foreach (var (row, index) in _lines.Select((r, i) => (r, i)))
        {
            if (!row.TryRead(out var error))
            {
                ErrorText.Text = $"Riga {index + 1}: {error}";
                return null;
            }

            lines.Add(new InvoiceLineRequestDto(row.Code, row.Description.Trim(), row.Quantity, row.Unit, row.UnitPrice, row.Discount,
                row.VatRate, row.VatRate == 0 ? row.VatNature : null, row.TransportDocumentId));
        }

        return new SaveInvoiceDto(customer.Id, (PaymentCombo.SelectedValue as string) ?? "MP05",
            DueDatePicker.SelectedDate.HasValue ? DateTime.SpecifyKind(DueDatePicker.SelectedDate.Value.Date, DateTimeKind.Utc) : null,
            string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(), lines);
    }

    private async Task<bool> SaveAsync()
    {
        if (BuildRequest() is not { } request)
        {
            return false;
        }

        Show(await _apiClient.SaveInvoiceAsync(_invoiceId, request));
        Changed = true;
        return true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await RunAsync(SaveAsync);

    private async void Issue_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            if (!await SaveAsync())
            {
                return false;
            }

            var prompt = new DatePromptWindow("Emetti fattura", "Data della fattura. Dopo l'emissione non si modifica più: gli errori si correggono con una nota di credito.", DateTime.Today) { Owner = this };
            if (prompt.ShowDialog() != true)
            {
                return false;
            }

            Show(await _apiClient.IssueInvoiceAsync(_invoiceId!.Value, prompt.From));
            Changed = true;
            if (MessageBox.Show($"Fattura {_invoice!.Code} emessa. Scaricare adesso il file XML?", "Fattura emessa", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                await SaveXmlAsync();
            }

            return true;
        });
    }

    private async void Xml_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { await SaveXmlAsync(); return true; });

    private async Task SaveXmlAsync()
    {
        var (fileName, content) = await _apiClient.DownloadInvoiceXmlAsync(_invoiceId!.Value);
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = fileName, Filter = "Fattura elettronica (*.xml)|*.xml" };
        if (dialog.ShowDialog(this) == true)
        {
            await File.WriteAllBytesAsync(dialog.FileName, content);
            MessageBox.Show($"Salvato {Path.GetFileName(dialog.FileName)}.\n\nCaricalo sul portale \"Fatture e Corrispettivi\" dell'Agenzia delle Entrate (gratuito) oppure consegnalo al commercialista o al tuo intermediario per l'invio allo SdI.",
                "File XML", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void Pdf_Click(object sender, RoutedEventArgs e)
    {
        if (_invoice is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"Fattura-{_invoice.Code.Replace('/', '-')}.pdf", Filter = "File PDF (*.pdf)|*.pdf" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var fiscal = await _apiClient.GetCustomerFiscalAsync(_invoice.CustomerId);
            var companyFiscal = await _apiClient.GetCompanyFiscalAsync();
            ListExporter.ExportInvoice(_invoice, fiscal, _apiClient.CompanyProfile, companyFiscal, dialog.FileName);
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            return true;
        });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_invoiceId is not { } id || MessageBox.Show("Eliminare la bozza? I DDT tornano da fatturare.", "Elimina bozza", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _apiClient.DeleteInvoiceAsync(id);
            Changed = true;
            Close();
            return true;
        });
    }

    private async Task RunAsync(Func<Task<bool>> action)
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

    /// <summary>A line edited in place; numbers are kept as typed and read with the Italian rules.</summary>
    public sealed class InvoiceLineRow : INotifyPropertyChanged
    {
        private static readonly string[] NatureCodes = ["N1", "N2.1", "N2.2", "N3.1", "N3.2", "N3.3", "N3.4", "N3.5", "N3.6", "N4", "N5", "N6.1", "N6.3", "N6.7", "N6.9", "N7"];
        private readonly Action _changed;
        private string _quantityText = "1";
        private string _priceText = string.Empty;
        private string _discountText = "0";
        private decimal _vatRate = 22;
        private string? _vatNature;

        public InvoiceLineRow(Action changed, InvoiceLineDto? line = null)
        {
            _changed = changed;
            if (line is null)
            {
                return;
            }

            Code = line.Code;
            Description = line.Description;
            Unit = line.Unit;
            _quantityText = line.Quantity.ToString("0.###", Italian);
            _priceText = line.UnitPrice.ToString("0.00##", Italian);
            _discountText = line.DiscountPercent.ToString("0.##", Italian);
            _vatRate = line.VatRate;
            _vatNature = line.VatNature;
            TransportDocumentId = line.TransportDocumentId;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<decimal> VatRates { get; } = [22m, 10m, 5m, 4m, 0m];
        public IReadOnlyList<string> Natures => NatureCodes;
        public string? Code { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Unit { get; set; } = "pz";
        public Guid? TransportDocumentId { get; }
        public bool IsEditable { get; init; }
        public bool NatureEditable => IsEditable && VatRate == 0;

        public string QuantityText { get => _quantityText; set { _quantityText = value; Changed(); } }
        public string PriceText { get => _priceText; set { _priceText = value; Changed(); } }
        public string DiscountText { get => _discountText; set { _discountText = value; Changed(); } }

        public decimal VatRate
        {
            get => _vatRate;
            set
            {
                _vatRate = value;
                if (value != 0)
                {
                    _vatNature = null;
                    OnPropertyChanged(nameof(VatNature));
                }

                OnPropertyChanged(nameof(NatureEditable));
                Changed();
            }
        }

        public string? VatNature { get => _vatNature; set { _vatNature = value; OnPropertyChanged(); } }

        public decimal Quantity => NumberInput.TryParseDecimal(QuantityText, out var v) ? v : 0;
        public decimal UnitPrice => NumberInput.TryParseDecimal(PriceText, out var v) ? v : 0;
        public decimal Discount => string.IsNullOrWhiteSpace(DiscountText) ? 0 : NumberInput.TryParseDecimal(DiscountText, out var v) ? v : 0;
        public decimal LineTotal => Math.Round(Quantity * UnitPrice * (1 - Discount / 100m), 2, MidpointRounding.AwayFromZero);
        public string LineTotalText => LineTotal.ToString("N2", Italian);

        public bool TryRead(out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(Description))
            {
                error = "manca la descrizione.";
            }
            else if (!NumberInput.TryParseDecimal(QuantityText, out var quantity) || quantity <= 0)
            {
                error = "quantità non valida.";
            }
            else if (!NumberInput.TryParseDecimal(string.IsNullOrWhiteSpace(PriceText) ? "0" : PriceText, out _))
            {
                error = "prezzo non valido.";
            }
            else if (!string.IsNullOrWhiteSpace(DiscountText) && (!NumberInput.TryParseDecimal(DiscountText, out var discount) || discount > 100))
            {
                error = "sconto non valido.";
            }
            else if (VatRate == 0 && string.IsNullOrWhiteSpace(VatNature))
            {
                error = "con IVA 0% scegli la natura.";
            }

            return error.Length == 0;
        }

        private void Changed([CallerMemberName] string? name = null)
        {
            OnPropertyChanged(name);
            OnPropertyChanged(nameof(LineTotalText));
            _changed();
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
