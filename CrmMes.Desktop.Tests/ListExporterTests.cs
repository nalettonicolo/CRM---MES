using System.IO;
using ClosedXML.Excel;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

public class ListExporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"crmmes-export-test-{Guid.NewGuid():N}");

    public ListExporterTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed record Row(string Code, string Name, decimal Stock);

    private static readonly ExportColumn[] Columns =
    [
        new("Codice", row => ((Row)row).Code),
        new("Descrizione", row => ((Row)row).Name),
        new("Giacenza", row => ((Row)row).Stock.ToString("0.##")),
    ];

    private static readonly Row[] Rows =
    [
        new("MAT-1", "Materiale uno", 10m),
        new("MAT-2", "Materiale due", 2.5m),
    ];

    [Fact]
    public void ExportToExcel_WritesHeaderAndDataRows()
    {
        var path = Path.Combine(_dir, "export.xlsx");

        ListExporter.ExportToExcel("Materiali", Columns, Rows, path);

        Assert.True(File.Exists(path));
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        Assert.Equal("Codice", sheet.Cell(1, 1).GetString());
        Assert.Equal("Descrizione", sheet.Cell(1, 2).GetString());
        Assert.Equal("Giacenza", sheet.Cell(1, 3).GetString());
        Assert.Equal("MAT-1", sheet.Cell(2, 1).GetString());
        Assert.Equal("Materiale uno", sheet.Cell(2, 2).GetString());
        Assert.Equal("10", sheet.Cell(2, 3).GetString());
        Assert.Equal("MAT-2", sheet.Cell(3, 1).GetString());
        Assert.Equal("2,5", sheet.Cell(3, 3).GetString().Replace('.', ','));
    }

    [Fact]
    public void ExportToExcel_EmptyRows_StillWritesHeaderOnly()
    {
        var path = Path.Combine(_dir, "empty.xlsx");

        ListExporter.ExportToExcel("Materiali", Columns, [], path);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        Assert.Equal("Codice", sheet.Cell(1, 1).GetString());
        Assert.True(string.IsNullOrEmpty(sheet.Cell(2, 1).GetString()));
    }

    [Fact]
    public void ExportToExcel_TruncatesLongSheetNameTo31Characters()
    {
        var path = Path.Combine(_dir, "longname.xlsx");
        var longTitle = new string('A', 50);

        ListExporter.ExportToExcel(longTitle, Columns, Rows, path);

        using var workbook = new XLWorkbook(path);
        Assert.True(workbook.Worksheets.First().Name.Length <= 31);
    }

    [Fact]
    public void ExportToPdf_ProducesNonEmptyFile()
    {
        var path = Path.Combine(_dir, "export.pdf");

        ListExporter.ExportToPdf("Materiali", Columns, Rows, path);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0);
        // %PDF magic bytes: proof this is a real PDF, not an empty/corrupt stub.
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
