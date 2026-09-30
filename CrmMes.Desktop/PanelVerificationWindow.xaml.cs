using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>CEI EN 61439 routine verification of a work order's panel, and its declaration of conformity
/// (panel builders' module). Editable until completed; completing needs every check done and none failed.</summary>
public partial class PanelVerificationWindow : Window
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;
    private readonly bool _isAdmin;
    private PanelVerificationDto? _verification;
    private List<CheckRow> _checks = [];

    public PanelVerificationWindow(ApiClient apiClient, Guid workOrderId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        _isAdmin = apiClient.CurrentRole == "Admin";
        Loaded += async (_, _) => await RunAsync(() => _apiClient.GetPanelVerificationAsync(_workOrderId));
    }

    private void Show(PanelVerificationDto verification)
    {
        _verification = verification;
        var editable = !verification.IsCompleted;

        HeaderText.Text = $"Verifica individuale · {verification.WorkOrderCode}";
        SubHeaderText.Text = $"{verification.ProductCode} {verification.ProductName}"
            + (verification.CustomerName is null ? string.Empty : $" · {verification.CustomerName}")
            + (verification.IsCompleted ? $" · completata il {verification.CompletedAt?.ToLocalTime():dd/MM/yyyy} da {verification.VerifiedBy}" : string.Empty);
        StatusText.Text = verification.IsCompleted ? "COMPLETATA" : verification.IsSaved ? "IN CORSO" : "DA INIZIARE";
        StatusText.Foreground = (Brush)new StatusToBrushConverter().Convert(
            verification.IsCompleted ? "Completed" : verification.IsSaved ? "InProgress" : "Draft", typeof(Brush), "Foreground", Italian);

        Select(StandardCombo, verification.Standard);
        OriginalManufacturerBox.Text = verification.OriginalManufacturer ?? string.Empty;
        SystemReferenceBox.Text = verification.SystemReference ?? string.Empty;
        SerialNumberBox.Text = verification.SerialNumber ?? verification.ProductLotNumber ?? string.Empty;
        Select(EarthingCombo, verification.EarthingSystem);
        VoltageBox.Text = Format(verification.RatedVoltage);
        CurrentBox.Text = Format(verification.RatedCurrent);
        FrequencyBox.Text = Format(verification.RatedFrequency);
        IcwBox.Text = Format(verification.ShortTimeWithstandCurrent);
        IccBox.Text = Format(verification.ConditionalShortCircuitCurrent);
        IpBox.Text = verification.IpRating ?? string.Empty;
        Select(SeparationCombo, verification.InternalSeparation);
        InsulationBox.Text = Format(verification.InsulationResistanceMOhm);
        TestVoltageBox.Text = Format(verification.DielectricTestVoltage);
        NotesBox.Text = verification.Notes ?? string.Empty;

        _checks = verification.Checks.Select(check => new CheckRow
        {
            Clause = check.Clause,
            Description = check.Description,
            Result = check.Result ?? string.Empty,
            Notes = check.Notes,
            IsEditable = editable
        }).ToList();
        ChecksList.ItemsSource = _checks;

        DataPanel.IsEnabled = editable;
        SaveButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        CompleteButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        ReopenButton.Visibility = !editable && _isAdmin ? Visibility.Visible : Visibility.Collapsed;
        PdfButton.IsEnabled = verification.IsCompleted;
        PdfButton.ToolTip = verification.IsCompleted ? null : "Disponibile a verifica completata";
    }

    private SavePanelVerificationDto? BuildRequest()
    {
        ErrorText.Text = string.Empty;
        var values = new Dictionary<string, decimal?>();
        foreach (var (name, box) in new[]
        {
            ("tensione", VoltageBox), ("corrente", CurrentBox), ("frequenza", FrequencyBox), ("Icw", IcwBox),
            ("Icc", IccBox), ("resistenza d'isolamento", InsulationBox), ("tensione di prova", TestVoltageBox)
        })
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                values[name] = null;
            }
            else if (NumberInput.TryParseDecimal(box.Text, out var value) && value >= 0)
            {
                values[name] = value;
            }
            else
            {
                ErrorText.Text = $"Valore non valido: {name}.";
                box.Focus();
                return null;
            }
        }

        return new SavePanelVerificationDto(
            Chosen(StandardCombo) ?? "CEI EN 61439-2", Clean(OriginalManufacturerBox.Text), Clean(SystemReferenceBox.Text), Clean(SerialNumberBox.Text),
            values["tensione"], values["corrente"], values["frequenza"], values["Icw"], values["Icc"],
            Clean(IpBox.Text), Chosen(SeparationCombo), Chosen(EarthingCombo),
            values["resistenza d'isolamento"], values["tensione di prova"], Clean(NotesBox.Text),
            _checks.Select(check => new PanelVerificationCheckDto(
                check.Clause, check.Description, string.IsNullOrEmpty(check.Result) ? null : check.Result, Clean(check.Notes))).ToList());
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (BuildRequest() is { } request)
        {
            await RunAsync(() => _apiClient.SavePanelVerificationAsync(_workOrderId, request));
        }
    }

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (BuildRequest() is not { } request)
        {
            return;
        }

        if (MessageBox.Show("Completare la verifica? Dati e esiti non saranno più modificabili (solo un Admin può riaprirla).",
                "Completa verifica", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _apiClient.SavePanelVerificationAsync(_workOrderId, request);
            return await _apiClient.CompletePanelVerificationAsync(_workOrderId);
        });
    }

    private async void Reopen_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Riaprire la verifica? La dichiarazione già stampata non sarà più valida finché non viene completata di nuovo.",
                "Riapri verifica", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await RunAsync(() => _apiClient.ReopenPanelVerificationAsync(_workOrderId));
        }
    }

    private void Pdf_Click(object sender, RoutedEventArgs e)
    {
        if (_verification is not { IsCompleted: true } verification)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"Dichiarazione-61439-{verification.WorkOrderCode}.pdf",
            Filter = "File PDF (*.pdf)|*.pdf"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ListExporter.ExportPanelDeclaration(verification, _apiClient.CompanyProfile, dialog.FileName);
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RunAsync(Func<Task<PanelVerificationDto>> action)
    {
        IsEnabled = false;
        try
        {
            Show(await action());
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

    /// <summary>Selects the item showing <paramref name="value"/>, adding it when a saved value isn't
    /// among the standard choices (so nothing typed in the past is lost).</summary>
    private static void Select(ComboBox combo, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            combo.SelectedIndex = 0;
            return;
        }

        var item = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Content as string) == value);
        if (item is null)
        {
            item = new ComboBoxItem { Content = value };
            combo.Items.Add(item);
        }

        combo.SelectedItem = item;
    }

    /// <summary>The chosen text, or null for the "(non indicato)" entry (Tag = "").</summary>
    private static string? Chosen(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: "" } ? null : Clean((combo.SelectedItem as ComboBoxItem)?.Content as string);

    private static string Format(decimal? value) => value?.ToString("0.###", Italian) ?? string.Empty;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>A checklist row edited in place (result combo and notes).</summary>
    public sealed class CheckRow
    {
        public string Clause { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public bool IsEditable { get; init; }
    }
}
