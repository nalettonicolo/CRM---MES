using System.Globalization;
using System.Text;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

/// <summary>METEL parser against synthetic files in both layouts the importers actually send
/// (fixed-width ANIE records and delimited Italian exports). A manufacturer file can still be used
/// later as an extra fixture; it is not required to accept the module.</summary>
public class MetelListTests
{
    [Fact]
    public void DelimitedFile_WithItalianHeaders_ReadsPriceAndEan()
    {
        var text = "Marca;Codice;Descrizione;UM;Prezzo;EAN\n" +
                   "ABB;1SDA067890R1;Interruttore 16A;PZ;12,50;8012345678901\n" +
                   "ABB;;riga senza codice;PZ;1;123\n";
        var parsed = MetelList.Parse(Encoding.UTF8.GetBytes(text));

        Assert.Single(parsed.Lines);
        Assert.Equal("ABB", parsed.Lines[0].Brand);
        Assert.Equal("1SDA067890R1", parsed.Lines[0].Code);
        Assert.Equal("Interruttore 16A", parsed.Lines[0].Name);
        Assert.Equal("pz", parsed.Lines[0].Unit);
        Assert.Equal(12.50m, parsed.Lines[0].ListPrice);
        Assert.Equal("8012345678901", parsed.Lines[0].Ean);
        Assert.Contains(parsed.Errors, error => error.Contains("codice o descrizione"));
    }

    [Fact]
    public void FixedWidthRecordA_UsesImpliedDecimalsAndSkipsTheHeader()
    {
        var header = "TABB LISTINO 2026";
        var article = "A" +
            "ABB" +
            "1S2016".PadRight(16) +
            "INTERRUTTORE MAGNETOTERMICO 16A".PadRight(40) +
            "PZ " +
            "00000001250" +
            "8012345678901";
        var parsed = MetelList.Parse(Encoding.Latin1.GetBytes(header + "\n" + article + "\n"));

        Assert.Empty(parsed.Errors);
        var line = Assert.Single(parsed.Lines);
        Assert.Equal("ABB", line.Brand);
        Assert.Equal("1S2016", line.Code);
        Assert.StartsWith("INTERRUTTORE MAGNETOTERMICO 16A", line.Name);
        Assert.Equal("pz", line.Unit);
        Assert.Equal(12.50m, line.ListPrice);
        Assert.Equal("8012345678901", line.Ean);
    }

    [Fact]
    public void EmptyFile_ReportsASingleError()
    {
        var parsed = MetelList.Parse(Encoding.UTF8.GetBytes("\n\n# commento\n"));
        Assert.Empty(parsed.Lines);
        Assert.Contains("vuoto", parsed.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DelimitedWithoutHeader_TakesBrandCodeName()
    {
        var text = "BTI|GW30001|Pulsante 1P|PZ|4.20\n";
        var parsed = MetelList.Parse(Encoding.UTF8.GetBytes(text));
        var line = Assert.Single(parsed.Lines);
        Assert.Equal("BTI", line.Brand);
        Assert.Equal("GW30001", line.Code);
        Assert.Equal(4.20m, line.ListPrice);
        Assert.Equal("pz", line.Unit);
    }
}
