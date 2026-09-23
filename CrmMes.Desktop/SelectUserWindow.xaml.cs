using System.Windows;

namespace CrmMes.Desktop;

public partial class SelectUserWindow : Window
{
    public Guid? SelectedUserId { get; private set; }

    public SelectUserWindow(IReadOnlyList<UserRowDto> candidates)
    {
        InitializeComponent();
        UserCombo.ItemsSource = candidates;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (UserCombo.SelectedItem is not UserRowDto user)
        {
            ErrorText.Text = "Seleziona un utente.";
            return;
        }

        SelectedUserId = user.Id;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
