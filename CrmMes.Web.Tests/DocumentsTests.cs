using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class DocumentsTests : TestContext
{
    private readonly FakeServer _server = new();

    public DocumentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private Task LogInAsync(string role) => Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    private static Invoice MakeInvoice(Guid id, string status) => new(
        id, "FT-2026-0001", status == "Issued" ? 1 : null, status == "Issued" ? 2026 : null, status, "TD24", Guid.NewGuid(), "Condominio Aurora",
        DateTime.UtcNow, "MP05", null, null,
        [new InvoiceLine(Guid.NewGuid(), 1, "QE-GEN", "Quadro generale BT", 2, "pz", 4800, 5, 22, null, 9120, null)],
        [new InvoiceDocument(Guid.NewGuid(), "DDT 1/2026", DateTime.UtcNow)],
        [new InvoiceVatLine(22, null, 9120, 2006.40m)], 11126.40m, [], DateTime.UtcNow, "Admin");

    [Fact]
    public async Task IssuedInvoice_DownloadsTheXml_WithTheServersFileName()
    {
        var id = Guid.NewGuid();
        await LogInAsync("Sales");
        _server.OnJson("GET", $"/api/invoices/{id}", MakeInvoice(id, "Issued"));
        _server.On("GET", $"/api/invoices/{id}/xml", _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<FatturaElettronica/>", Encoding.UTF8, "application/xml") };
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "IT01234567890_00001.xml" };
            return response;
        });
        var saved = JSInterop.SetupVoid("nicolomes.saveFile", _ => true);
        var page = RenderComponent<InvoicePage>(p => p.Add(x => x.Id, id));
        page.WaitForAssertion(() => Assert.Contains("11.126,40", page.Markup));
        Assert.Contains("Differita (da DDT)", page.Markup);

        page.FindAll("button").Single(b => b.TextContent == "Scarica XML FatturaPA").Click();

        page.WaitForAssertion(() => Assert.Single(saved.Invocations));
        var arguments = saved.Invocations.Single().Arguments;
        Assert.Equal("IT01234567890_00001.xml", arguments[0]);
        Assert.Equal("application/xml", arguments[1]);
        Assert.Equal("<FatturaElettronica/>", Encoding.UTF8.GetString(Convert.FromBase64String((string)arguments[2]!)));
    }

    [Fact]
    public async Task DraftInvoice_HasNoXml_AndSaysWhereToIssueIt()
    {
        var id = Guid.NewGuid();
        await LogInAsync("Admin");
        _server.OnJson("GET", $"/api/invoices/{id}", MakeInvoice(id, "Draft"));

        var page = RenderComponent<InvoicePage>(p => p.Add(x => x.Id, id));

        page.WaitForAssertion(() => Assert.Contains("si emette dal programma desktop", page.Markup));
        Assert.DoesNotContain("Scarica XML", page.Markup);
    }

    [Fact]
    public async Task Invoices_AreNotShownToTheShopFloor()
    {
        await LogInAsync("Operator");

        var page = RenderComponent<Invoices>();

        Assert.Contains("Non disponibile per il tuo ruolo", page.Markup);
        Assert.Empty(_server.Requests);
        Assert.DoesNotContain(Navigation.Visible(null, "Operator"), e => e.Href == "fatture");
    }

    [Fact]
    public async Task SubcontractingDocument_ShowsWhatStillHasToComeBack()
    {
        var id = Guid.NewGuid();
        await LogInAsync("Purchasing");
        var document = new TransportDocument(id, "12/2026", 12, 2026, "Issued", "Subcontracting", null, "Conto lavorazione",
            null, Guid.NewGuid(), "Zincatura Nord srl", "Via Po 3", null, null, "Sender", null, null, "Franco", null, 2, 150m,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(7), null, null, null, null, DateTime.UtcNow, "Acquisti", DateTime.UtcNow, "Acquisti", null,
            [new TransportDocumentLine(Guid.NewGuid(), 1, null, null, "TEL-01", "Telaio da zincare", 20, "pz", null, null, 15, 1, 4, [])]);
        _server.OnJson("GET", $"/api/transport-documents/{id}", document);

        var page = RenderComponent<TransportDocumentPage>(p => p.Add(x => x.Id, id));

        page.WaitForAssertion(() => Assert.Contains("Zincatura Nord srl", page.Markup));
        Assert.Contains("Da rientrare", page.Markup);
        Assert.Contains("badge warn\">4", page.Markup);
        Assert.Contains("Rientro previsto", page.Markup);
    }
}
