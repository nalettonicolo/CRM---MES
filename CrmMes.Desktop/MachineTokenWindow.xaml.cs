using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Shows a newly generated machine token once, with what the gateway needs to be configured.</summary>
public partial class MachineTokenWindow : Window
{
    public MachineTokenWindow(MachineTokenDto token, Guid equipmentId)
    {
        InitializeComponent();
        IdBox.Text = equipmentId.ToString();
        EndpointBox.Text = token.Endpoint;
        TokenLabel.Text = $"Token (intestazione {token.Header})";
        TokenBox.Text = token.Token;
    }

    private void CopyId_Click(object sender, RoutedEventArgs e) => CopySupport.Copy(IdBox.Text);

    private void CopyEndpoint_Click(object sender, RoutedEventArgs e) => CopySupport.Copy(EndpointBox.Text);

    private void CopyToken_Click(object sender, RoutedEventArgs e) => CopySupport.Copy(TokenBox.Text);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
