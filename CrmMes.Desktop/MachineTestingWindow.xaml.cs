using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>Collaudo e CE di una commessa (modulo "machine-testing"): collaudi FAT/SAT con la loro checklist,
/// il fascicolo tecnico (Reg. UE 2023/1230) e la dichiarazione di conformità. Eseguire e chiudere un collaudo
/// è aperto a chiunque sia autenticato, come il resto del lavoro di reparto; il fascicolo tecnico e la
/// dichiarazione restano all'ufficio tecnico (Admin, Direzione); riaprire un collaudo chiuso e ritirare una
/// dichiarazione emessa sono operazioni solo Admin — la stessa regola dell'API.</summary>
public partial class MachineTestingWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;
    private MachineDossierDto? _dossier;
    private Guid? _selectedTestId;

    private sealed class TestRow(MachineTestDto source)
    {
        public Guid Id { get; } = source.Id;
        public string NumberLabel { get; } = $"#{source.Number}";
        public string Kind { get; } = source.Kind;
        public string SerialNumber { get; } = source.SerialNumber ?? "—";
        public string TestDateLabel { get; } = source.TestDate?.ToString("dd/MM/yyyy") ?? "—";
        public string StatusLabel { get; } = source.Status switch { "Draft" => "In corso", "Passed" => "Superato", "Failed" => "Non superato", _ => source.Status };
        public string TestedBy { get; } = source.TestedBy ?? "—";
    }

    private sealed class ChecklistRow(CrmMes.Desktop.MachineTestItemDto source, bool editable)
    {
        public string Section { get; } = source.Section;
        public string Description { get; } = source.Description;
        public string Expected { get; } = source.Expected ?? "—";
        public string? Measured { get; set; } = source.Measured;
        public string Result { get; set; } = source.Result ?? string.Empty;
        public string Notes { get; set; } = source.Notes ?? string.Empty;
        public bool IsEditable { get; } = editable;
    }

    private sealed class FileRow(TechnicalFileItemDto source)
    {
        public string Code { get; } = source.Code;
        public string Description { get; } = source.Description;
        public string OptionalLabel { get; } = source.Optional ? "Sì" : "No";
        public string Status { get; set; } = source.Status ?? string.Empty;
        public string Reference { get; set; } = source.Reference ?? string.Empty;
    }

    public MachineTestingWindow(ApiClient apiClient, Guid workOrderId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        var isEngineer = apiClient.CurrentRole is "Admin" or "Management";
        SaveTechnicalFileButton.Visibility = isEngineer ? Visibility.Visible : Visibility.Collapsed;
        SaveDeclarationButton.Visibility = IssueDeclarationButton.Visibility = isEngineer ? Visibility.Visible : Visibility.Collapsed;
        WithdrawDeclarationButton.Visibility = apiClient.CurrentRole == "Admin" ? Visibility.Visible : Visibility.Collapsed;
        ReopenTestButton.Visibility = apiClient.CurrentRole == "Admin" ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        ErrorText.Text = string.Empty;
        try
        {
            _dossier = await _apiClient.GetMachineDossierAsync(_workOrderId);
            ApplyDossier();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void ApplyDossier()
    {
        if (_dossier is null)
        {
            return;
        }

        HeaderText.Text = $"{_dossier.WorkOrderCode} · {_dossier.ProductName}";
        SubHeaderText.Text = $"Cliente: {_dossier.CustomerName ?? "—"} · Prodotto: {_dossier.ProductCode}" + (_dossier.ProductRevision is null ? "" : $" rev. {_dossier.ProductRevision}");
        DeclarationStatusText.Text = _dossier.Declaration.Status == "Issued" ? $"Dichiarazione n. {_dossier.Declaration.Number}" : "Dichiarazione in bozza";

        TestsList.ItemsSource = _dossier.Tests.Select(t => new TestRow(t)).ToList();
        _selectedTestId = null;
        ChecklistList.ItemsSource = null;
        SaveTestButton.IsEnabled = CloseTestButton.IsEnabled = ReopenTestButton.IsEnabled = false;

        TechnicalFileList.ItemsSource = _dossier.TechnicalFile.Select(f => new FileRow(f)).ToList();

        var declaration = _dossier.Declaration;
        MachineNameBox.Text = declaration.MachineName;
        FunctionBox.Text = declaration.Function ?? string.Empty;
        ModelBox.Text = declaration.Model ?? string.Empty;
        DeclarationSerialBox.Text = declaration.SerialNumber ?? string.Empty;
        YearBox.Text = declaration.YearOfConstruction?.ToString() ?? string.Empty;
        PlaceBox.Text = declaration.Place ?? string.Empty;
        SignatoryNameBox.Text = declaration.SignatoryName ?? string.Empty;
        SignatoryRoleBox.Text = declaration.SignatoryRole ?? string.Empty;

        var issued = declaration.Status == "Issued";
        foreach (var box in new[] { MachineNameBox, FunctionBox, ModelBox, DeclarationSerialBox, YearBox, PlaceBox, SignatoryNameBox, SignatoryRoleBox })
        {
            box.IsEnabled = !issued;
        }

        SaveDeclarationButton.IsEnabled = !issued;
        IssueDeclarationButton.Visibility = issued ? Visibility.Collapsed : IssueDeclarationButton.Visibility;
        WithdrawDeclarationButton.Visibility = issued && _apiClient.CurrentRole == "Admin" ? Visibility.Visible : Visibility.Collapsed;
        IssuedInfoText.Text = issued
            ? $"Emessa il {declaration.IssuedAt?.ToLocalTime():dd/MM/yyyy} da {declaration.IssuedBy} — base giuridica: {declaration.LegalBasisText}"
            : $"Base giuridica che verrà applicata se emessa oggi: {declaration.LegalBasisText}";

        MissingText.Text = _dossier.MissingForDeclaration.Count == 0
            ? string.Empty
            : "Per emettere la dichiarazione manca: " + string.Join(", ", _dossier.MissingForDeclaration);
    }

    private void TestsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TestsList.SelectedItem is not TestRow row || _dossier is null)
        {
            _selectedTestId = null;
            SaveTestButton.IsEnabled = CloseTestButton.IsEnabled = ReopenTestButton.IsEnabled = false;
            return;
        }

        var test = _dossier.Tests.Single(t => t.Id == row.Id);
        _selectedTestId = test.Id;
        var editable = test.Status == "Draft";
        ChecklistList.ItemsSource = test.Items.Select(i => new ChecklistRow(i, editable)).ToList();
        SaveTestButton.IsEnabled = editable;
        CloseTestButton.IsEnabled = editable;
        ReopenTestButton.IsEnabled = !editable;
    }

    private async void NewTest_Click(object sender, RoutedEventArgs e)
    {
        var kind = (NewTestKindCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "FAT";
        try
        {
            ErrorText.Text = string.Empty;
            _dossier = await _apiClient.CreateMachineTestAsync(_workOrderId, kind, NewTestSerialBox.Text, null);
            NewTestSerialBox.Text = string.Empty;
            ApplyDossier();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void SaveTest_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTestId is not { } testId || _dossier is null || ChecklistList.ItemsSource is not List<ChecklistRow> rows)
        {
            return;
        }

        var original = _dossier.Tests.Single(t => t.Id == testId);
        var updated = original with
        {
            Items = original.Items.Zip(rows, (item, row) => item with
            {
                Result = string.IsNullOrEmpty(row.Result) ? null : row.Result,
                Notes = string.IsNullOrEmpty(row.Notes) ? null : row.Notes,
                Measured = row.Measured,
            }).ToList(),
        };

        try
        {
            ErrorText.Text = string.Empty;
            _dossier = await _apiClient.SaveMachineTestAsync(testId, updated);
            ApplyDossier();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void CloseTest_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTestId is not { } testId)
        {
            return;
        }

        try
        {
            ErrorText.Text = string.Empty;
            _dossier = await _apiClient.CloseMachineTestAsync(testId);
            ApplyDossier();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void ReopenTest_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTestId is not { } testId)
        {
            return;
        }

        try
        {
            ErrorText.Text = string.Empty;
            _dossier = await _apiClient.ReopenMachineTestAsync(testId);
            ApplyDossier();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void SaveTechnicalFile_Click(object sender, RoutedEventArgs e)
    {
        if (TechnicalFileList.ItemsSource is not List<FileRow> rows || _dossier is null)
        {
            return;
        }

        var items = _dossier.TechnicalFile.Zip(rows, (source, row) => new TechnicalFileItemDto(
            source.Code, source.Description, source.Optional,
            string.IsNullOrEmpty(row.Status) ? null : row.Status,
            string.IsNullOrEmpty(row.Reference) ? null : row.Reference,
            source.UpdatedBy, source.UpdatedAt));

        try
        {
            ErrorText.Text = string.Empty;
            _dossier = await _apiClient.SaveTechnicalFileAsync(_workOrderId, items);
            ApplyDossier();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void SaveDeclaration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Text = string.Empty;
            _dossier = await _apiClient.SaveMachineDeclarationAsync(_workOrderId, BuildDeclaration());
            ApplyDossier();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void IssueDeclaration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Text = string.Empty;
            await _apiClient.SaveMachineDeclarationAsync(_workOrderId, BuildDeclaration());
            _dossier = await _apiClient.IssueMachineDeclarationAsync(_workOrderId);
            ApplyDossier();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void WithdrawDeclaration_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Ritirare la dichiarazione emessa? Torna in bozza e l'operazione resta nel registro.", "Ritira dichiarazione",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ErrorText.Text = string.Empty;
            _dossier = await _apiClient.WithdrawMachineDeclarationAsync(_workOrderId);
            ApplyDossier();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private SaveMachineDeclarationDto BuildDeclaration() => new(
        MachineNameBox.Text, Clean(FunctionBox.Text), Clean(ModelBox.Text), null, Clean(DeclarationSerialBox.Text),
        int.TryParse(YearBox.Text, out var year) ? year : null, null, null, null, null, Clean(PlaceBox.Text),
        Clean(SignatoryNameBox.Text), Clean(SignatoryRoleBox.Text), null);

    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
