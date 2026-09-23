using System.Windows;

namespace CrmMes.Desktop;

public partial class WorkOrderLabelWindow : Window
{
    private readonly string _code;
    private readonly string _productName;
    private readonly string _lotNumber;
    private readonly byte[] _qrPngBytes;

    public WorkOrderLabelWindow(string code, string productName, string lotNumber)
    {
        InitializeComponent();
        _code = code;
        _productName = productName;
        _lotNumber = lotNumber;
        _qrPngBytes = QrCodeHelper.GeneratePng(code);

        QrImage.Source = QrCodeHelper.ToBitmapImage(_qrPngBytes);
        CodeText.Text = code;
        ProductText.Text = productName;
        LotText.Text = $"Lotto {lotNumber}";
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"Etichetta-{_code}.pdf"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ListExporter.ExportWorkOrderLabel(_code, _productName, _lotNumber, _qrPngBytes, dialog.FileName);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
