using System.IO;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

/// <summary>Printed documents of the sector modules (DDT, 61439 declaration) and the client-side rules
/// behind what the UI shows (enabled modules, lot expiry, DDT labels).</summary>
public class SectorDocumentsTests : IDisposable
{
    // CRMMES_KEEP_TEST_PDF=<folder> keeps the generated PDFs there, to look at them.
    private static readonly string? KeepFolder = Environment.GetEnvironmentVariable("CRMMES_KEEP_TEST_PDF");
    private readonly string _dir = KeepFolder ?? Path.Combine(Path.GetTempPath(), $"crmmes-sector-test-{Guid.NewGuid():N}");

    public SectorDocumentsTests()
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

    private static readonly CompanyProfileDto Company = new(
        true, "Quadri Rossi srl", "IT01234567890", "Via Po 1, Torino", "011 000000", "info@example.invalid",
        "electrical-panels", ["sales", "shipping", "panel-verification"]);

    private static TransportDocumentDto Document(string status, string reason = "Sale") => new(
        Guid.NewGuid(), status == "Draft" ? "Bozza" : "7/2026", status == "Draft" ? null : 7, status == "Draft" ? null : 2026,
        status, reason, null, reason == "Subcontracting" ? "Conto lavorazione" : "Vendita", null, null,
        "Cliente Prova spa", "Via Garibaldi 3, Milano", "IT09876543210", null, "Carrier", null, "Corriere Veloce",
        "Franco", "Scatole", 2, 18.5m, DateTime.UtcNow, reason == "Subcontracting" ? DateTime.UtcNow.AddDays(10) : null,
        null, "WO-1", "Consegna al piano", status == "Cancelled" ? "Destinatario errato" : null,
        DateTime.UtcNow, "Admin", status == "Draft" ? null : DateTime.UtcNow, "Admin", status == "Cancelled" ? DateTime.UtcNow : null,
        [
            new(Guid.NewGuid(), 1, null, null, "QE-01", "Quadro elettrico QE-01", 1, "pz", "L-2026-01", null, null, null, null, []),
            new(Guid.NewGuid(), 2, null, null, null, "Chiavi e documentazione", 1, "set", null, "In busta", null, null, null, [])
        ]);

    [Theory]
    [InlineData("Draft", "Sale")]
    [InlineData("Issued", "Sale")]
    [InlineData("Issued", "Subcontracting")]
    [InlineData("Cancelled", "Sale")]
    public void ExportTransportDocument_WritesAPdfInEveryState(string status, string reason)
    {
        var path = Path.Combine(_dir, $"ddt-{status}-{reason}.pdf");
        ListExporter.ExportTransportDocument(Document(status, reason), Company, path);
        AssertIsPdf(path);
    }

    [Fact]
    public void ExportTransportDocument_WorksBeforeTheCompanyIsConfigured()
    {
        var path = Path.Combine(_dir, "ddt-no-company.pdf");
        ListExporter.ExportTransportDocument(Document("Issued"), null, path);
        AssertIsPdf(path);
    }

    [Fact]
    public void ExportPanelDeclaration_WritesDeclarationAndReport()
    {
        var verification = new PanelVerificationDto(
            true, "Completed", Guid.NewGuid(), "WO-1", "QE-01", "Quadro generale", "L-2026-01", "Cliente Prova spa",
            "CEI EN 61439-2", "Sistema XY", "Armadio XY-2000", "SN-001", 400, 250, 50, 25, null, "IP55", "Forma 2b", "TN-S",
            500, 2500, "Nessuna anomalia", DateTime.UtcNow, "Collaudatore",
            [
                new("11.2", "Grado di protezione degli involucri (IP)", "Pass", null),
                new("11.10", "Cablaggio, prestazioni operative e funzionamento", "NotApplicable", "Nessun ausiliario")
            ]);
        var path = Path.Combine(_dir, "dichiarazione.pdf");
        ListExporter.ExportPanelDeclaration(verification, Company, path);
        AssertIsPdf(path);
    }

    [Fact]
    public void IsModuleEnabled_IsPermissiveUntilAProfileIsLoaded()
    {
        var client = new ApiClient();
        Assert.True(client.IsModuleEnabled("metel"));

        client.CompanyProfile = Company;
        Assert.True(client.IsModuleEnabled("Panel-Verification"));
        Assert.False(client.IsModuleEnabled("lot-expiry"));
    }

    [Fact]
    public void CanViewMargins_NeedsBothTheRoleAndTheCostingModule()
    {
        var client = new ApiClient { CurrentRole = "Management" };
        Assert.True(client.CanViewMargins); // no profile yet: everything on

        client.CompanyProfile = Company; // costing not among the enabled modules
        Assert.False(client.CanViewMargins);

        client.CompanyProfile = Company with { EnabledModules = ["costing"] };
        Assert.True(client.CanViewMargins);
        client.CurrentRole = "Operator";
        Assert.False(client.CanViewMargins);
    }

    [Fact]
    public void LotExpiry_FlagsExpiredAndSoonToExpireLotsStillInStock()
    {
        MaterialLotSummaryDto Lot(int? days, decimal quantity = 5) => new(
            Guid.NewGuid(), "LAT", "L1", quantity, 10, null, null, DateTime.UtcNow, days is null ? null : DateTime.Today.AddDays(days.Value));

        Assert.True(Lot(-1).IsExpired);
        Assert.False(Lot(-1).ExpiresSoon);
        Assert.True(Lot(10).ExpiresSoon);
        Assert.False(Lot(45).ExpiresSoon);
        Assert.False(Lot(null).IsExpired);
        Assert.False(Lot(-1, quantity: 0).IsExpired); // used up: nothing to warn about
    }

    [Theory]
    [InlineData("Draft", "Bozza")]
    [InlineData("Issued", "Emesso")]
    [InlineData("Cancelled", "Annullato")]
    public void TransportDocument_StatusLabelsAreItalian(string status, string expected) =>
        Assert.Equal(expected, TransportDocumentDto.StatusText(status));

    private static void AssertIsPdf(string path)
    {
        Assert.True(File.Exists(path));
        var header = new byte[5];
        using (var stream = File.OpenRead(path))
        {
            Assert.Equal(5, stream.Read(header, 0, 5));
        }

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(header));
        Assert.True(new FileInfo(path).Length > 1000);
    }
}
