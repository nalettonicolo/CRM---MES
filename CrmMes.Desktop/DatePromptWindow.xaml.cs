using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Asks for one date (optionally empty) or, with <c>to</c>, a period of whole days.</summary>
public partial class DatePromptWindow : Window
{
    private readonly bool _allowEmpty;
    private readonly bool _isRange;

    public DateTime? From { get; private set; }
    public DateTime? To { get; private set; }

    public DatePromptWindow(string title, string message, DateTime? from, bool allowEmpty = false, DateTime? to = null, bool isRange = false)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        _allowEmpty = allowEmpty;
        _isRange = isRange;
        FromPicker.SelectedDate = from;
        if (isRange)
        {
            FromLabel.Text = "Dal";
            ToPanel.Visibility = Visibility.Visible;
            ToPicker.SelectedDate = to;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty && FromPicker.SelectedDate is null || _isRange && ToPicker.SelectedDate is null)
        {
            ErrorText.Text = "Scegli la data.";
            return;
        }

        if (_isRange && ToPicker.SelectedDate < FromPicker.SelectedDate)
        {
            ErrorText.Text = "La data finale precede quella iniziale.";
            return;
        }

        From = FromPicker.SelectedDate?.Date;
        To = ToPicker.SelectedDate?.Date;
        DialogResult = true;
    }
}
