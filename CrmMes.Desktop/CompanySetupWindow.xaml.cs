using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>Company configuration in three steps. A company is rarely one trade, so: (1) company data and
/// every activity it has, (2) its departments, pre-ticked by the activities, renamed as the company calls
/// them, repeatable (two assembly lines), each with its typical work centers, (3) modules and services,
/// proposed by activities and departments with the reason next to each. Nothing is imposed: the Admin
/// can untick any proposal, and the choices can be changed later from Amministrazione.</summary>
public partial class CompanySetupWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly List<ActivityOption> _activities = [];
    private readonly List<ModuleOption> _modules = [];
    private readonly System.Collections.ObjectModel.ObservableCollection<DepartmentOption> _departments = [];
    private IReadOnlyList<DepartmentCatalogDto> _departmentCatalog = [];
    private bool _modulesTouched;
    private bool _isConfigured;
    private int _step = 1;

    public CompanyProfileDto? SavedProfile { get; private set; }

    public CompanySetupWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        NextButton.IsEnabled = false;
        try
        {
            var catalog = await _apiClient.GetCompanyCatalogAsync();
            var profile = _apiClient.CompanyProfile ?? await _apiClient.GetCompanyProfileAsync();
            var existing = await _apiClient.GetCompanyStructureAsync();
            _departmentCatalog = catalog.Departments ?? [];
            _isConfigured = profile is { IsConfigured: true };

            _activities.AddRange(catalog.Sectors.Select(sector => new ActivityOption(sector)));
            _modules.AddRange(catalog.Modules.OrderBy(module => module.SectorSpecific).Select(module => new ModuleOption(module)));

            var chosen = _isConfigured ? profile!.Activities ?? [profile.Sector] : [];
            foreach (var activity in _activities)
            {
                activity.IsSelected = chosen.Contains(activity.Key, StringComparer.OrdinalIgnoreCase);
            }

            // Every department type once, plus the departments already configured (a second assembly line
            // appears as its own row). Configured ones are ticked with their real names.
            foreach (var info in _departmentCatalog)
            {
                var configured = existing.Where(d => d.Type == info.Key).ToList();
                if (configured.Count == 0)
                {
                    _departments.Add(new DepartmentOption(info, info.Name, existing: null));
                }

                foreach (var department in configured)
                {
                    _departments.Add(new DepartmentOption(info, department.Name, department) { IsIncluded = true, CreateWorkCenters = false });
                }
            }

            if (_isConfigured)
            {
                CompanyNameBox.Text = profile!.CompanyName;
                VatNumberBox.Text = profile.VatNumber ?? string.Empty;
                AddressBox.Text = profile.Address ?? string.Empty;
                PhoneBox.Text = profile.Phone ?? string.Empty;
                EmailBox.Text = profile.Email ?? string.Empty;
                Gs1PrefixBox.Text = profile.Gs1CompanyPrefix ?? string.Empty;
                foreach (var module in _modules)
                {
                    module.IsChecked = module.Available && profile.EnabledModules.Contains(module.Key, StringComparer.OrdinalIgnoreCase);
                }

                _modulesTouched = true; // an existing configuration is kept as it is unless the Admin changes it
            }

            ActivityList.ItemsSource = _activities;
            DepartmentList.ItemsSource = _departments;
            ModuleList.ItemsSource = _modules;
            AddDepartmentCombo.ItemsSource = _departmentCatalog;
            AddDepartmentCombo.SelectedIndex = _departmentCatalog.Count > 0 ? 0 : -1;
            ShowStep(1);
            NextButton.IsEnabled = true;
            CompanyNameBox.Focus();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void ShowStep(int step)
    {
        _step = step;
        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (label, number) in new[] { (Step1Label, 1), (Step2Label, 2), (Step3Label, 3) })
        {
            label.SetResourceReference(TextBlock.ForegroundProperty, number == step ? "PrimaryBrush" : "TextMutedBrush");
        }

        BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = step < 3 ? "Avanti" : "Salva configurazione";
        ErrorText.Text = string.Empty;
    }

    private IEnumerable<SectorDto> ChosenActivities => _activities.Where(a => a.IsSelected).Select(a => a.Sector);

    /// <summary>Only a choice made now proposes departments, and it never unticks what the Admin kept.</summary>
    private void Activity_Click(object sender, RoutedEventArgs e)
    {
        var suggested = CompanyStructurePlanner.SuggestedDepartments(ChosenActivities);
        foreach (var department in _departments.Where(d => !d.IsExisting))
        {
            if (suggested.Contains(department.Type) && !department.WasTouched)
            {
                department.IsIncluded = true;
            }
        }

        RefreshModuleProposals();
    }

    private void Department_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: DepartmentOption department })
        {
            department.WasTouched = true;
        }

        RefreshModuleProposals();
    }

    private void Module_Click(object sender, RoutedEventArgs e) => _modulesTouched = true;

    private void AddDepartment_Click(object sender, RoutedEventArgs e)
    {
        if (AddDepartmentCombo.SelectedItem is not DepartmentCatalogDto info)
        {
            return;
        }

        var name = CompanyStructurePlanner.UniqueName(info.Name, _departments.Where(d => d.IsIncluded).Select(d => d.Name));
        var sameTypeUnticked = _departments.FirstOrDefault(d => d.Type == info.Key && !d.IsIncluded && !d.IsExisting);
        if (sameTypeUnticked is not null)
        {
            sameTypeUnticked.IsIncluded = true;
            sameTypeUnticked.WasTouched = true;
        }
        else
        {
            var index = _departments.ToList().FindLastIndex(d => d.Type == info.Key);
            var option = new DepartmentOption(info, name, existing: null) { IsIncluded = true, WasTouched = true };
            _departments.Insert(index < 0 ? _departments.Count : index + 1, option);
        }

        RefreshModuleProposals();
    }

    /// <summary>Reasons are always shown; ticks follow the proposals until the Admin touches a module.</summary>
    private void RefreshModuleProposals()
    {
        var reasons = CompanyStructurePlanner.SuggestedModules(
            ChosenActivities,
            _departments.Where(d => d.IsIncluded).Select(d => (d.Type, d.Name)),
            _departmentCatalog,
            _modules.Select(m => m.Module).ToList());
        foreach (var module in _modules)
        {
            module.Reasons = reasons.TryGetValue(module.Key, out var list) ? list : [];
            if (!_modulesTouched)
            {
                module.IsChecked = module.Available && module.Reasons.Count > 0;
            }
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(Math.Max(1, _step - 1));

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (_step == 1)
        {
            if (string.IsNullOrWhiteSpace(CompanyNameBox.Text))
            {
                ErrorText.Text = "Inserisci la ragione sociale.";
                CompanyNameBox.Focus();
                return;
            }

            if (!ChosenActivities.Any())
            {
                ErrorText.Text = "Scegli almeno un'attività.";
                return;
            }

            RefreshModuleProposals();
            ShowStep(2);
            return;
        }

        if (_step == 2)
        {
            var included = _departments.Where(d => d.IsIncluded).ToList();
            if (included.Any(d => string.IsNullOrWhiteSpace(d.Name)))
            {
                ErrorText.Text = "Dai un nome a ogni reparto spuntato.";
                return;
            }

            var duplicate = included.GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                ErrorText.Text = $"Il nome \"{duplicate.Key}\" è usato da due reparti: rendili diversi (es. \"{duplicate.Key} 2\").";
                return;
            }

            RefreshModuleProposals();
            ShowStep(3);
            return;
        }

        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        NextButton.IsEnabled = false;
        try
        {
            var activities = ChosenActivities.Select(a => a.Key).ToList();
            SavedProfile = await _apiClient.SaveCompanyProfileAsync(new SaveCompanyProfileDto(
                CompanyNameBox.Text.Trim(),
                NullIfEmpty(VatNumberBox.Text),
                NullIfEmpty(AddressBox.Text),
                NullIfEmpty(PhoneBox.Text),
                NullIfEmpty(EmailBox.Text),
                activities[0],
                _modules.Where(module => module.IsChecked).Select(module => module.Key).ToList(),
                activities));

            var departments = _departments.Where(d => d.IsIncluded)
                .Select(d => new SaveDepartmentDto(d.Type, d.Name.Trim(), d.SiteId, d.CreateWorkCenters && d.HasWorkCenters))
                .ToList();
            if (departments.Count > 0)
            {
                await _apiClient.SaveCompanyStructureAsync(departments);
            }

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
            NextButton.IsEnabled = true;
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
                Notify(name);
            }
        }

        protected void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class ActivityOption(SectorDto sector) : Observable
    {
        private bool _isSelected;

        public SectorDto Sector { get; } = sector;
        public string Key => Sector.Key;
        public string Name => Sector.Name;
        public string Description => Sector.Description;

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }

    public sealed class DepartmentOption : Observable
    {
        private bool _isIncluded;
        private bool _createWorkCenters = true;
        private string _name;

        public DepartmentOption(DepartmentCatalogDto info, string name, DepartmentDto? existing)
        {
            Info = info;
            _name = name;
            Existing = existing;
        }

        public DepartmentCatalogDto Info { get; }
        public DepartmentDto? Existing { get; }
        public string Type => Info.Key;
        public string TypeName => Info.Name;
        public string Description => Info.Description;
        public bool IsExisting => Existing is not null;
        public Guid? SiteId => Existing?.SiteId;
        public string ExistingText => Existing is null ? string.Empty : $"Già presente · {Existing.WorkCenterCount} centri di lavoro";
        public bool HasWorkCenters => Info.WorkCenters.Count > 0;
        public string WorkCentersText => "Crea i centri tipici: " + string.Join(", ", Info.WorkCenters.Select(w => w.Name));
        public bool WasTouched { get; set; }

        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        public bool IsIncluded
        {
            get => _isIncluded;
            set => Set(ref _isIncluded, value);
        }

        public bool CreateWorkCenters
        {
            get => _createWorkCenters;
            set => Set(ref _createWorkCenters, value);
        }
    }

    public sealed class ModuleOption(ModuleDto module) : Observable
    {
        private bool _isChecked;
        private List<string> _reasons = [];

        public ModuleDto Module { get; } = module;
        public string Key => Module.Key;
        public string Name => Module.Name;
        public string Description => Module.Description;
        public bool SectorSpecific => Module.SectorSpecific;
        public bool Available => Module.Available;
        public bool ComingSoon => !Available;

        public List<string> Reasons
        {
            get => _reasons;
            set
            {
                _reasons = value;
                Notify(nameof(Reasons));
                Notify(nameof(ReasonText));
                Notify(nameof(HasReason));
            }
        }

        public bool HasReason => _reasons.Count > 0;
        public string ReasonText => "Proposto da: " + string.Join(", ", _reasons);

        public bool IsChecked
        {
            get => _isChecked;
            set => Set(ref _isChecked, value);
        }
    }
}
