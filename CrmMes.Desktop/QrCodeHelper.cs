using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace CrmMes.Desktop;

/// <summary>Turns a short piece of text (here, a work order code) into a printable/scannable QR code —
/// the pairing for <see cref="ShopFloorTerminalWindow"/>: a label printed from the office with this QR
/// is what an operator scans at the machine to pull the job up on the terminal.</summary>
public static class QrCodeHelper
{
    public static byte[] GeneratePng(string content, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(qrData);
        return pngQrCode.GetGraphic(pixelsPerModule);
    }

    public static BitmapImage ToBitmapImage(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
