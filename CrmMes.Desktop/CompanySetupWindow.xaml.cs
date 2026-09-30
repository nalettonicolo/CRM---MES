using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>First-start configuration (and later changes, from Amministrazione): company data, industry
/// and enabled modules. Choosing a sector resets the modules to that sector's preset; the Admin can then
/// switch single modules on or off.</summary>
public partial class CompanySetupWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly List<SectorOption> _sectors = [];
    private readonly List<ModuleOption> _modules = [];
    public CompanyProfileDto? SavedProfile { get; private set; }

    public CompanySetupWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SaveButton.IsEnabled = false;
        try
        {
            var catalog = await _apiClient.GetCompanyCatalogAsync();
            var profile = _apiClient.CompanyProfile ?? await _apiClient.GetCompanyProfileAsync();

            _sectors.AddRange(catalog.Sectors.Select(sector => new SectorOption(sector)));
            // Common modules first, sector-specific ones after: the order of the catalog within each group.
            _modules.AddRange(catalog.Modules.OrderBy(module => module.SectorSpecific).Select(module => new ModuleOption(module)));

            if (profile is { IsConfigured: true })
            {
                CompanyNameBox.Text = profile.CompanyName;
                VatNumberBox.Text = profile.VatNumber ?? string.Empty;
                AddressBox.Text = profile.Address ?? string.Empty;
                PhoneBox.Text = profile.Phone ?? string.Empty;
                EmailBox.Text = profile.Email ?? string.Empty;
                Gs1PrefixBox.Text = profile.Gs1CompanyPrefix ?? string.Empty;
                SelectSector(profile.Sector);
                foreach (var module in _modules)
                {
                    module.IsChecked = module.Available && profile.EnabledModules.Contains(module.Key, StringComparer.OrdinalIgnoreCase);
                }
            }
            else
            {
                SelectSector("generic");
                ApplySectorPreset(_sectors.First(sector => sector.IsSelected));
            }

            SectorList.ItemsSource = _sectors;
            ModuleList.ItemsSource = _modules;
            SaveButton.IsEnabled = true;
            CompanyNameBox.Focus();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void SelectSector(string key)
    {
        var match = _sectors.FirstOrDefault(sector => sector.Key == key) ?? _sectors.Last();
        foreach (var sector in _sectors)
        {
            sector.IsSelected = sector == match;
        }
    }

    private void ApplySectorPreset(SectorOption sector)
    {
        foreach (var module in _modules)
        {
            module.IsChecked = module.Available && sector.Modules.Contains(module.Key);
        }
    }

    // Click, not Checked: only a choice made by the user resets the modules, never the selection
    // restored from an existing profile when the cards are first shown.
    private void Sector_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: SectorOption sector })
        {
            ApplySectorPreset(sector);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(CompanyNameBox.Text))
        {
            ErrorText.Text = "Inserisci la ragione sociale.";
            CompanyNameBox.Focus();
            return;
        }

        var sector = _sectors.FirstOrDefault(option => option.IsSelected);
        if (sector is null)
        {
            ErrorText.Text = "Scegli il settore.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            SavedProfile = await _apiClient.SaveCompanyProfileAsync(new SaveCompanyProfileDto(
                CompanyNameBox.Text.Trim(),
                NullIfEmpty(VatNumberBox.Text),
                NullIfEmpty(AddressBox.Text),
                NullIfEmpty(PhoneBox.Text),
                NullIfEmpty(EmailBox.Text),
                sector.Key,
                _modules.Where(module => module.IsChecked).Select(module => module.Key).ToList()));
            var prefix = Gs1PrefixBox.Text.Trim();
            if (prefix.Length > 0 && prefix != SavedProfile.Gs1CompanyPrefix)
            {
                await _apiClient.SetGs1PrefixAsync(prefix);
                SavedProfile = SavedProfile with { Gs1CompanyPrefix = prefix };
            }

            Close();
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

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    public sealed class SectorOption(SectorDto sector) : Observable
    {
        private bool _isSelected;

        public string Key { get; } = sector.Key;
        public string Name { get; } = sector.Name;
        public string Description { get; } = sector.Description;
        public IReadOnlyList<string> Modules { get; } = sector.Modules;

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }

    public sealed class ModuleOption(ModuleDto module) : Observable
    {
        private bool _isChecked;

        public string Key { get; } = module.Key;
        public string Name { get; } = module.Name;
        public string Description { get; } = module.Description;
        public bool SectorSpecific { get; } = module.SectorSpecific;
        public bool Available { get; } = module.Available;
        public bool ComingSoon => !Available;

        public bool IsChecked
        {
            get => _isChecked;
            set => Set(ref _isChecked, value);
        }
    }
}
