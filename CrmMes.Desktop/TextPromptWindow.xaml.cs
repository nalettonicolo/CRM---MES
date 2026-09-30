using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Asks for one required line of text (e.g. why a document is being cancelled).</summary>
public partial class TextPromptWindow : Window
{
    public string Value { get; private set; } = string.Empty;

    /// <summary>When true an empty answer is accepted (e.g. "whole quantity").</summary>
    public bool AllowEmpty { get; set; }

    public TextPromptWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        Loaded += (_, _) => ValueBox.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowEmpty && string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            ErrorText.Text = "Campo obbligatorio.";
            return;
        }

        Value = ValueBox.Text.Trim();
        DialogResult = true;
    }
}
