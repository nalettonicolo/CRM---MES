using System.Windows;

namespace CrmMes.Desktop;

public partial class SetUserPinWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _userId;

    public bool PinSet { get; private set; }

    public SetUserPinWindow(ApiClient apiClient, Guid userId, string userName)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _userId = userId;
        UserNameText.Text = userName;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var pin = PinBox.Password;

        if (pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit))
        {
            ErrorText.Text = "Il PIN deve essere numerico, da 4 a 8 cifre.";
            return;
        }

        if (pin != ConfirmPinBox.Password)
        {
            ErrorText.Text = "I due PIN inseriti non coincidono.";
            return;
        }

        try
        {
            await _apiClient.SetUserPinAsync(_userId, pin);
            PinSet = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
