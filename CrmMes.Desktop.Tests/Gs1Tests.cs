using System.IO;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

/// <summary>GS1 numbering and GS1-128 barcodes, plus the food, pallet, recall and site report PDFs.</summary>
public class Gs1Tests : IDisposable
{
    private static readonly string? KeepFolder = Environment.GetEnvironmentVariable("CRMMES_KEEP_TEST_PDF");
    private readonly string _dir = KeepFolder ?? Path.Combine(Path.GetTempPath(), $"crmmes-gs1-test-{Guid.NewGuid():N}");

    public Gs1Tests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (KeepFolder is null && Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Code128Table_HasElevenModulesPerSymbol_AndNoDuplicates()
    {
        Assert.Equal(107, Gs1Barcode.Patterns.Length);
        for (var value = 0; value < 106; value++)
        {
            Assert.Equal(11, Gs1Barcode.Patterns[value].Sum(c => c - '0'));
            Assert.Equal(6, Gs1Barcode.Patterns[value].Length);
        }

        Assert.Equal(13, Gs1Barcode.Patterns[106].Sum(c => c - '0'));   // stop
        Assert.Equal(107, Gs1Barcode.Patterns.Distinct().Count());
    }

    [Fact]
    public void SsccBarcode_IsStartCFnc1PairsAndTheHandComputedCheck()
    {
        // Values: Start C 105, FNC1 102, pairs 00 10 61 41 41 12 34 56 78 97.
        // Check = (105 + 102*1 + 0*2 + 10*3 + 61*4 + 41*5 + 41*6 + 12*7 + 34*8 + 56*9 + 78*10 + 97*11) mod 103
        //       = 3639 mod 103 = 34.
        var values = Gs1Barcode.Encode([Gs1Barcode.SsccElement("106141411234567897")]);
        Assert.Equal([105, 102, 0, 10, 61, 41, 41, 12, 34, 56, 78, 97, 34], values);

        var modules = Gs1Barcode.Modules([Gs1Barcode.SsccElement("106141411234567897")]);
        Assert.Equal(13 * 11 + 13, modules.Sum());
        Assert.Contains("<svg", Gs1Barcode.Svg([Gs1Barcode.SsccElement("106141411234567897")]));
    }

    [Fact]
    public void VariableLengthFields_SwitchToSetB_AndGetASeparator()
    {
        var values = Gs1Barcode.Encode([Gs1Barcode.CountElement(24), Gs1Barcode.LotElement("L1")]);
        // 105 102 | "37" "24" | FNC1 | "10" | Code B | 'L' '1' | check
        Assert.Equal([105, 102, 37, 24, 102, 10, 100, 'L' - 32, '1' - 32], values.Take(values.Count - 1));
        Assert.Throws<ArgumentException>(() => Gs1Barcode.LotElement(new string('X', 21)));
        Assert.Throws<ArgumentException>(() => Gs1Barcode.SsccElement("106141411234567890"));   // wrong check digit
        Assert.Equal("(15) 261031 (10) AB12", Gs1Barcode.HumanReadable([Gs1Barcode.BestBeforeElement(new DateTime(2026, 10, 31)), Gs1Barcode.LotElement("AB12")]));
    }

    [Fact]
    public void CheckDigit_MatchesPublishedExamples()
    {
        Assert.Equal(1, Gs1Barcode.CheckDigit("400638133393"));      // EAN-13 4006381333931
        Assert.Equal(7, Gs1Barcode.CheckDigit("10614141123456789")); // SSCC 106141411234567897
        Assert.True(Gs1Barcode.IsValidSscc("106141411234567897"));
    }

    [Fact]
    public void FoodPalletRecallAndSiteReportPdfs_AreGenerated()
    {
        var company = new CompanyProfileDto(true, "Alimentari Verdi srl", "IT01234567890", "Via Emilia 10, Parma", null, null, "food", ["food-labels"], "8012345");
        var label = new FoodLabelDto(
            Guid.NewGuid(), "WO-1", "BRI", "Brioche al latte", "L-2026-01", 100, DateTime.Today, DateTime.Today.AddDays(20), false,
            "Conservare in luogo fresco e asciutto", "400 g", company.CompanyName, company.Address,
            [new("FAR", "farina di grano tenero tipo 00", 1, ["gluten"], ["Cereali contenenti glutine"]),
             new("LAT", "latte intero", 0.3m, ["milk"], ["Latte"]),
             new("SAL", "sale", 0.02m, [], [])],
            ["Cereali contenenti glutine", "Latte"], []);
        AssertPdf(path => ListExporter.ExportFoodLabel(label, path), "etichetta.pdf");

        var pallet = new LogisticUnitDto(Guid.NewGuid(), "080123450000000425", Guid.NewGuid(), null, "BRI", "Brioche al latte",
            "L-2026-01", 240, DateTime.Today.AddDays(20), DateTime.UtcNow);
        Assert.True(Gs1Barcode.IsValidSscc(pallet.Sscc));
        AssertPdf(path => ListExporter.ExportPalletLabel(pallet, company, path), "pallet.pdf");

        var recall = new RecallDto("Lotto materiale SOSP-1 (FAR)",
            [new(Guid.NewGuid(), "WO-1", "BRI", "Brioche", "L-2026-01", 100, "Completed", 20, "Panetteria Rossi", ["S-1", "S-2"])],
            [new("080123450000000425", "L-2026-01", 240, DateTime.UtcNow)],
            [new(Guid.NewGuid(), "12/2026", DateTime.UtcNow, "Panetteria Rossi", "C1", "Via Roma 1", "BRI", "Brioche", 100, "pz", "L-2026-01")],
            ["Panetteria Rossi"], ["Ancora in magazzino: 20 del lotto, da bloccare."]);
        AssertPdf(path => ListExporter.ExportRecall(recall, company, path), "richiamo.pdf");

        var png = new byte[400];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        var report = new SiteReportDto(Guid.NewGuid(), "RI-2026-0001", Guid.NewGuid(), "WO-9", "Impianto", "Condominio Aurora", "Draft",
            DateTime.Today, "Via Milano 5, Bergamo", "Posa linea montante", null, null, null, null, "Luca", DateTime.UtcNow,
            [new("Luca Bassi", null, null, 240)], [new("CAV", "Cavo FG16 3x2,5", 35, "m")]);
        AssertPdf(path => ListExporter.ExportSiteReport(report, company, path), "rapportino-bozza.pdf");
    }

    private void AssertPdf(Action<string> export, string name)
    {
        var path = Path.Combine(_dir, name);
        export(path);
        var header = new byte[5];
        using (var stream = File.OpenRead(path))
        {
            Assert.Equal(5, stream.Read(header, 0, 5));
        }

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(header));
        Assert.True(new FileInfo(path).Length > 1000);
    }
}
