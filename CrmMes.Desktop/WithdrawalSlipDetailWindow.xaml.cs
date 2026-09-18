using System.Windows;

namespace CrmMes.Desktop;

public partial class WithdrawalSlipDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _slipId;
    private readonly IReadOnlyDictionary<Guid, string> _areaNames;
    private readonly IReadOnlyDictionary<Guid, string> _userNames;
    private static readonly StatusToBrushConverter StatusBrush = new();

    public WithdrawalSlipDetailWindow(
        ApiClient apiClient,
        Guid slipId,
        IReadOnlyDictionary<Guid, string> areaNames,
        IReadOnlyDictionary<Guid, string> userNames)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _slipId = slipId;
        _areaNames = areaNames;
        _userNames = userNames;
        Loaded += WithdrawalSlipDetailWindow_Loaded;
    }

    private async void WithdrawalSlipDetailWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var slip = await _apiClient.GetWithdrawalSlipAsync(_slipId);

            CodeText.Text = slip.Code;
            StatusText.Text = slip.Status;
            StatusPill.Background = (System.Windows.Media.Brush)StatusBrush.Convert(slip.Status, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentCulture)!;
            StatusText.Foreground = (System.Windows.Media.Brush)StatusBrush.Convert(slip.Status, typeof(System.Windows.Media.Brush), "Foreground", System.Globalization.CultureInfo.CurrentCulture)!;

            AreaText.Text = _areaNames.GetValueOrDefault(slip.AreaId, slip.AreaId.ToString());
            RequestedByText.Text = _userNames.GetValueOrDefault(slip.RequestedByUserId, slip.RequestedByUserId.ToString());
            CreatedAtText.Text = slip.CreatedAt.ToLocalTime().ToString("g");
            NotesText.Text = string.IsNullOrWhiteSpace(slip.Notes) ? "-" : slip.Notes;

            ItemsList.ItemsSource = slip.Items;

            LoadingText.Visibility = Visibility.Collapsed;
            HeaderInfo.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            LoadingText.Text = $"Impossibile caricare la distinta: {exception.Message}";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
