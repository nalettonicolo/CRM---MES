using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

public class StatusToBrushConverterTests
{
    private readonly StatusToBrushConverter _converter = new();

    [Fact]
    public void Convert_WithoutForegroundParameter_ReturnsTransparent()
    {
        var result = _converter.Convert("Completed", typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Equal(Brushes.Transparent.Color, Assert.IsType<SolidColorBrush>(result).Color);
    }

    [Theory]
    [InlineData("Draft", "#8C7F6A")]
    [InlineData("Cancelled", "#C0392B")]
    [InlineData("Done", "#3D7A4C")]
    [InlineData("InProgress", "#C57821")]
    public void Convert_WithForegroundParameter_ReturnsExpectedColorForKnownStatus(string status, string expectedHex)
    {
        var result = _converter.Convert(status, typeof(Brush), "Foreground", CultureInfo.InvariantCulture);

        var expected = (Color)ColorConverter.ConvertFromString(expectedHex);
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(result).Color);
    }

    [Fact]
    public void Convert_IsCaseInsensitiveOnStatusKey()
    {
        var lower = _converter.Convert("cancelled", typeof(Brush), "Foreground", CultureInfo.InvariantCulture);
        var mixed = _converter.Convert("Cancelled", typeof(Brush), "Foreground", CultureInfo.InvariantCulture);

        Assert.Equal(((SolidColorBrush)mixed).Color, ((SolidColorBrush)lower).Color);
    }

    [Fact]
    public void Convert_UnknownStatus_FallsBackToNeutralGray()
    {
        var result = _converter.Convert("SomeUnknownStatus", typeof(Brush), "Foreground", CultureInfo.InvariantCulture);

        var expected = (Color)ColorConverter.ConvertFromString("#8C7F6A");
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(result).Color);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(Brushes.Transparent, typeof(string), null, CultureInfo.InvariantCulture));
    }
}

public class StatusToItalianTextConverterTests
{
    [Theory]
    [InlineData("Draft", "Bozza")]
    [InlineData("Done", "Completata")]
    [InlineData("Completed", "Completata")]
    [InlineData("Cancelled", "Annullata")]
    public void Translate_KnownStatus_ReturnsItalianLabel(string status, string expected)
    {
        Assert.Equal(expected, StatusToItalianTextConverter.Translate(status));
    }

    [Fact]
    public void Translate_UnknownStatus_ReturnsStatusUnchanged()
    {
        Assert.Equal("QualcosaDiNuovo", StatusToItalianTextConverter.Translate("QualcosaDiNuovo"));
    }

    [Fact]
    public void Translate_Null_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, StatusToItalianTextConverter.Translate(null));
    }

    [Fact]
    public void Convert_DelegatesToTranslate()
    {
        var converter = new StatusToItalianTextConverter();

        var result = converter.Convert("Pending", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("In attesa", result);
    }
}

public class BoolToTextConverterTests
{
    private readonly BoolToTextConverter _converter = new();

    [Fact]
    public void Convert_True_DefaultsToSi()
    {
        Assert.Equal("Sì", _converter.Convert(true, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_False_DefaultsToNo()
    {
        Assert.Equal("No", _converter.Convert(false, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_WithCustomParameter_SplitsOnPipe()
    {
        Assert.Equal("Attivo", _converter.Convert(true, typeof(string), "Attivo|Inattivo", CultureInfo.InvariantCulture));
        Assert.Equal("Inattivo", _converter.Convert(false, typeof(string), "Attivo|Inattivo", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_NonBooleanValue_TreatedAsFalse()
    {
        Assert.Equal("No", _converter.Convert("not a bool", typeof(string), null, CultureInfo.InvariantCulture));
    }
}

public class BoolToBrushConverterTests
{
    private readonly BoolToBrushConverter _converter = new();

    [Fact]
    public void Convert_WithoutForegroundParameter_ReturnsTransparent()
    {
        var result = _converter.Convert(true, typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Equal(Brushes.Transparent.Color, Assert.IsType<SolidColorBrush>(result).Color);
    }

    [Fact]
    public void Convert_False_Foreground_ReturnsMutedGray()
    {
        var result = _converter.Convert(false, typeof(Brush), "Foreground", CultureInfo.InvariantCulture);

        var expected = (Color)ColorConverter.ConvertFromString("#8C7F6A");
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(result).Color);
    }

    [Fact]
    public void Convert_TrueWarning_ReturnsAmber()
    {
        var result = _converter.Convert(true, typeof(Brush), "Foreground_warning", CultureInfo.InvariantCulture);

        var expected = (Color)ColorConverter.ConvertFromString("#C57821");
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(result).Color);
    }

    [Fact]
    public void Convert_TrueNonWarning_ReturnsAccent()
    {
        var result = _converter.Convert(true, typeof(Brush), "Foreground", CultureInfo.InvariantCulture);

        var expected = (Color)ColorConverter.ConvertFromString("#B36F1B");
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(result).Color);
    }
}

public class OperationStatusToActionTextConverterTests
{
    private readonly OperationStatusToActionTextConverter _converter = new();

    [Theory]
    [InlineData("Pending", "Avvia")]
    [InlineData("InProgress", "Completa")]
    [InlineData("Done", "")]
    [InlineData(null, "")]
    public void Convert_ReturnsExpectedLabel(string? status, string expected)
    {
        Assert.Equal(expected, _converter.Convert(status, typeof(string), null, CultureInfo.InvariantCulture));
    }
}

public class StatusNotEqualToVisibilityConverterTests
{
    private readonly StatusNotEqualToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_EqualStatus_ReturnsCollapsed()
    {
        var result = _converter.Convert("Done", typeof(Visibility), "Done", CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_EqualStatus_IsCaseInsensitive()
    {
        var result = _converter.Convert("done", typeof(Visibility), "Done", CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_DifferentStatus_ReturnsVisible()
    {
        var result = _converter.Convert("Pending", typeof(Visibility), "Done", CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Visible, result);
    }
}
