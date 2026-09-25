using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

public partial class CreatePlanningProjectWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly PlanningProjectDto? _existing;

    public bool Created { get; private set; }

    public CreatePlanningProjectWindow(ApiClient apiClient, PlanningProjectDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _existing = existing;

        if (existing is not null)
        {
            TitleText.Text = "Modifica progetto macchina";
            SaveButton.Content = "Salva modifiche";
            DeactivateButton.Visibility = Visibility.Visible;
            NameBox.Text = existing.Name;
            NotesBox.Text = existing.Notes ?? "";
            foreach (ComboBoxItem item in StatusCombo.Items)
            {
                if (item.Tag as string == existing.Status)
                {
                    StatusCombo.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Il nome è obbligatorio.";
            return;
        }

        var status = (StatusCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "InValutazione";
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        SaveButton.IsEnabled = false;
        try
        {
            if (_existing is null)
            {
                await _apiClient.CreatePlanningProjectAsync(name, status, notes, null);
            }
            else
            {
                await _apiClient.EditPlanningProjectAsync(_existing.Id, name, status, notes, _existing.WorkOrderId);
            }

            Created = true;
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

    private async void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"Disattivare \"{_existing.Name}\" dal planning? Le settimane dipinte restano salvate ma la riga sparisce dalla vista.",
                "Conferma disattivazione", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeactivatePlanningProjectAsync(_existing.Id);
            Created = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
