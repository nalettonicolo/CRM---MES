using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>Small circled "?" badge used throughout the app next to a page title or a non-obvious
/// field. Hovering it shows the explanation as a tooltip (native WPF behaviour); clicking it also shows
/// the same text in a message box, for anyone who prefers a click over hovering.</summary>
public partial class HelpIcon : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(HelpIcon), new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public HelpIcon()
    {
        InitializeComponent();
    }

    private void IconButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(Text, "Informazioni", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
