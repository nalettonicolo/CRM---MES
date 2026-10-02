using System.Net;
using System.Net.Http.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

/// <summary>Electronic invoices: from DDTs to an XML that the official FatturaPA schema (1.2.3, FPR12)
/// accepts, with the checks the Exchange System would otherwise reject the invoice for.</summary>
public class InvoicesTests : IClassFixture<AdminSeededApiTestFixture>
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new(LoadSchemas);
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public InvoicesTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static XmlSchemaSet LoadSchemas()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Schemas");
        var set = new XmlSchemaSet { XmlResolver = null };
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        using (var dsig = XmlReader.Create(Path.Combine(folder, "xmldsig-core-schema.xsd"), settings))
        {
            set.Add("http://www.w3.org/2000/09/xmldsig#", dsig);
        }

        using (var fattura = XmlReader.Create(Path.Combine(folder, "Schema_VFPR12_v1.2.3.xsd"), settings))
        {
            set.Add(FatturaPa.Ns.NamespaceName, fattura);
        }

        set.Compile();
        return set;
    }

    private static List<string> SchemaErrors(XDocument document)
    {
        var errors = new List<string>();
        document.Validate(Schemas.Value, (_, e) => errors.Add($"{e.Severity}: {e.Message}"));
        return errors;
    }

    private static void AssertValidAgainstSchema(string xml)
    {
        var errors = SchemaErrors(XDocument.Parse(xml));
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private async Task ConfigureCompanyAsync()
    {
        await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Impianti Rossi srl", "IT01234567890", "Via Po 1, Torino", null, null, "installations", null));
        var response = await _admin.PutAsJsonAsync("/api/company-profile/fiscal", new CompanyFiscalResponse(
            "01234567890", "RF01", "Via Po 1", "10123", "Torino", "to", "IT", "TO", "123456", "IT60 X054 2811 1010 0000 0123 456"));
        response.EnsureSuccessStatusCode();
    }

    private async Task<CustomerResponse> CreateCustomerAsync(bool withFiscal)
    {
        var code = $"C-{Guid.NewGuid():N}"[..10];
        var customer = (await (await _admin.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest($"Condominio Aurora {code}", code, "IT09876543210", null, null, "Via Milano 5, Bergamo", null)))
            .Content.ReadFromJsonAsync<CustomerResponse>())!;
        if (withFiscal)
        {
            (await _admin.PutAsJsonAsync($"/api/customers/{customer.Id}/fiscal", new CustomerFiscalResponse(
                null, "M5UXCR1", null, "Via Milano 5", "24121", "Bergamo", "BG", "IT"))).EnsureSuccessStatusCode();
        }

        return customer;
    }

    private async Task<TransportDocumentResponse> IssuedDdtForSoldJobAsync(Guid customerId, decimal quantity, decimal salePrice)
    {
        var product = (await (await _admin.PostAsJsonAsync("/api/products", new CreateProductRequest($"QE-{Guid.NewGuid():N}"[..10], "Quadro di piano", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        var order = (await (await _admin.PostAsJsonAsync("/api/work-orders",
            new CreateWorkOrderRequest(product.Id, quantity, null, null, null, null, null, CustomerId: customerId)))
            .Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        (await _admin.PutAsJsonAsync($"/api/work-orders/{order.Id}/sale-price", new SetSalePriceRequest(salePrice))).EnsureSuccessStatusCode();
        var draft = (await (await _admin.PostAsync($"/api/transport-documents/from-work-order/{order.Id}", null))
            .Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
        var issued = await _admin.PostAsJsonAsync($"/api/transport-documents/{draft.Id}/issue", new IssueTransportDocumentRequest(null));
        issued.EnsureSuccessStatusCode();
        return (await issued.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
    }

    [Fact]
    public async Task DeferredInvoice_FromDdts_IsPricedChecked_AndProducesASchemaValidXml()
    {
        await ConfigureCompanyAsync();
        var customer = await CreateCustomerAsync(withFiscal: false);
        var first = await IssuedDdtForSoldJobAsync(customer.Id, 2, 3000);
        var second = await IssuedDdtForSoldJobAsync(customer.Id, 1, 1250.5m);

        var pending = (await _admin.GetFromJsonAsync<List<UninvoicedDocumentResponse>>($"/api/invoices/uninvoiced-transport-documents?customerId={customer.Id}"))!;
        Assert.Equal(2, pending.Count);

        var draftResponse = await _admin.PostAsJsonAsync("/api/invoices/from-transport-documents", new CreateInvoiceFromDocumentsRequest([first.Id, second.Id]));
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = (await draftResponse.Content.ReadFromJsonAsync<InvoiceResponse>())!;
        Assert.Equal("TD24", draft.DocumentType);
        Assert.Equal(2, draft.Lines.Count);
        Assert.Equal(1500m, draft.Lines[0].UnitPrice);           // 3000 sold for 2 pieces
        Assert.Equal(4250.5m, draft.Lines.Sum(l => l.LineTotal));
        Assert.Equal(Math.Round(4250.5m * 1.22m, 2), draft.Total);
        Assert.Contains(draft.Warnings, w => w.Contains("15 del mese successivo"));

        // The same DDTs can't go on a second invoice.
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsJsonAsync("/api/invoices/from-transport-documents",
            new CreateInvoiceFromDocumentsRequest([first.Id]))).StatusCode);
        Assert.Empty((await _admin.GetFromJsonAsync<List<UninvoicedDocumentResponse>>($"/api/invoices/uninvoiced-transport-documents?customerId={customer.Id}"))!);

        // Customer tax data missing: the SdI would reject it, so issuing is refused with the reason.
        var refused = await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/issue", new IssueInvoiceRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("dati fiscali", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.GetAsync($"/api/invoices/{draft.Id}/xml")).StatusCode);

        (await _admin.PutAsJsonAsync($"/api/customers/{customer.Id}/fiscal", new CustomerFiscalResponse(
            null, "m5uxcr1", null, "Via Milano 5", "24121", "Bergamo", "bg", "IT"))).EnsureSuccessStatusCode();
        var issuedResponse = await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/issue", new IssueInvoiceRequest(null));
        issuedResponse.EnsureSuccessStatusCode();
        var issued = (await issuedResponse.Content.ReadFromJsonAsync<InvoiceResponse>())!;
        Assert.Equal("Issued", issued.Status);
        Assert.NotNull(issued.Number);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"/api/invoices/{draft.Id}", new SaveInvoiceRequest(
            null, "MP05", null, null, [new InvoiceLineRequest(null, "x", 1, "pz", 1, 0, 22, null, null)]))).StatusCode);

        var xmlResponse = await _admin.GetAsync($"/api/invoices/{draft.Id}/xml");
        xmlResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("IT01234567890_", xmlResponse.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var xml = await xmlResponse.Content.ReadAsStringAsync();
        AssertValidAgainstSchema(xml);

        var doc = XDocument.Parse(xml);
        string Value(string name) => doc.Descendants(name).First().Value;
        Assert.Equal("M5UXCR1", Value("CodiceDestinatario"));
        Assert.Equal("TD24", Value("TipoDocumento"));
        Assert.Equal("5185.61", Value("ImportoTotaleDocumento"));   // 4250.50 + 22% = 935.11
        Assert.Equal("935.11", Value("Imposta"));
        Assert.Equal(2, doc.Descendants("DatiDDT").Count());
        Assert.Equal(first.DocumentCode, doc.Descendants("DatiDDT").First().Element("NumeroDDT")!.Value);
        Assert.Equal("IT60X0542811101000000123456", Value("IBAN"));
        Assert.Equal("TO", Value("Ufficio"));
    }

    [Fact]
    public async Task IssueInvoice_XmlUsesCompanyCurrency()
    {
        await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Impianti Rossi srl", "IT01234567890", "Via Po 1, Torino", null, null, "installations", null, null, "it-IT", "CHF"));
        (await _admin.PutAsJsonAsync("/api/company-profile/fiscal", new CompanyFiscalResponse(
            "01234567890", "RF01", "Via Po 1", "10123", "Torino", "TO", "IT", "TO", "123456", "IT60 X054 2811 1010 0000 0123 456"))).EnsureSuccessStatusCode();

        var customer = await CreateCustomerAsync(withFiscal: true);
        var ddt = await IssuedDdtForSoldJobAsync(customer.Id, 1, 100m);
        var draft = (await (await _admin.PostAsJsonAsync("/api/invoices/from-transport-documents", new CreateInvoiceFromDocumentsRequest([ddt.Id])))
            .Content.ReadFromJsonAsync<InvoiceResponse>())!;
        (await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/issue", new IssueInvoiceRequest(null))).EnsureSuccessStatusCode();

        var xml = await (await _admin.GetAsync($"/api/invoices/{draft.Id}/xml")).Content.ReadAsStringAsync();
        Assert.Equal("CHF", XDocument.Parse(xml).Descendants("Divisa").First().Value);
    }

    [Fact]
    public async Task SdiStatus_TracksProviderAndManualPortalFlow_AndRejectsInvalidTransitions()
    {
        await ConfigureCompanyAsync();
        var customer = await CreateCustomerAsync(withFiscal: true);
        var draft = (await (await _admin.PostAsJsonAsync("/api/invoices", new SaveInvoiceRequest(
            customer.Id, "MP05", null, null, [new InvoiceLineRequest(null, "Quadro elettrico", 1, "pz", 100, 0, 22, null, null)])))
            .Content.ReadFromJsonAsync<InvoiceResponse>())!;
        (await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/issue", new IssueInvoiceRequest(null))).EnsureSuccessStatusCode();

        var issued = (await _admin.GetFromJsonAsync<InvoiceResponse>($"/api/invoices/{draft.Id}"))!;
        Assert.Equal(SdiStatuses.NotSent, issued.SdiStatus);
        var submit = await _admin.PostAsync($"/api/invoices/{draft.Id}/sdi/submit", null);
        submit.EnsureSuccessStatusCode();
        var inTransit = (await submit.Content.ReadFromJsonAsync<InvoiceResponse>())!;
        Assert.Equal(SdiStatuses.Submitted, inTransit.SdiStatus);
        Assert.StartsWith("STUB-", inTransit.SdiTransmissionId);
        Assert.Contains("simulato", inTransit.SdiMessage);

        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "sdi-op");
        var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync($"/api/invoices/{draft.Id}/sdi/status",
            new RecordSdiStatusRequest(SdiStatuses.Accepted, inTransit.SdiTransmissionId, "Ricevuta"))).StatusCode);

        var accepted = await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/sdi/status",
            new RecordSdiStatusRequest(SdiStatuses.Accepted, inTransit.SdiTransmissionId, "Consegnata"));
        accepted.EnsureSuccessStatusCode();
        Assert.Equal(SdiStatuses.Accepted, (await accepted.Content.ReadFromJsonAsync<InvoiceResponse>())!.SdiStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/sdi/status",
            new RecordSdiStatusRequest(SdiStatuses.Rejected, "PORTALE-123", "Esito già definitivo"))).StatusCode);
    }

    [Fact]
    public async Task ManualInvoice_WithReverseCharge_SummarisesEachRateAndValidates()
    {
        await ConfigureCompanyAsync();
        var customer = await CreateCustomerAsync(withFiscal: true);
        var response = await _admin.PostAsJsonAsync("/api/invoices", new SaveInvoiceRequest(customer.Id, "MP05", DateTime.UtcNow.Date.AddDays(30), "Lavori di giugno",
        [
            new InvoiceLineRequest("MAN", "Manodopera impianto elettrico (subappalto)", 16, "h", 38.5m, 0, 0, "N6.3", null),
            new InvoiceLineRequest("CAV", "Cavo FG16 3x2,5", 120, "m", 1.2345m, 10, 22, null, null),
        ]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<InvoiceResponse>())!;
        Assert.Equal("TD01", draft.DocumentType);
        Assert.Equal(2, draft.VatSummary.Count);

        (await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/issue", new IssueInvoiceRequest(null))).EnsureSuccessStatusCode();
        var xml = await _admin.GetStringAsync($"/api/invoices/{draft.Id}/xml");
        AssertValidAgainstSchema(xml);
        var doc = XDocument.Parse(xml);
        var summaries = doc.Descendants("DatiRiepilogo").ToList();
        Assert.Equal(2, summaries.Count);
        var reverse = summaries.Single(s => s.Element("Natura")?.Value == "N6.3");
        Assert.Equal("616.00", reverse.Element("ImponibileImporto")!.Value);
        Assert.Equal("0.00", reverse.Element("Imposta")!.Value);
        Assert.NotNull(reverse.Element("RiferimentoNormativo"));
        Assert.Equal("1.2345", doc.Descendants("PrezzoUnitario").Last().Value);
        Assert.Equal("133.33", doc.Descendants("PrezzoTotale").Last().Value);   // 120 x 1.2345 - 10% = 133.326

        // Positive control: the same validator rejects the file once a required element is missing or
        // a value breaks its format, so the passes above are real checks, not a validator that accepts all.
        var missingCurrency = XDocument.Parse(xml);
        missingCurrency.Descendants("Divisa").Single().Remove();
        Assert.NotEmpty(SchemaErrors(missingCurrency));
        var badAmount = XDocument.Parse(xml);
        badAmount.Descendants("ImportoTotaleDocumento").Single().Value = "12,50";
        Assert.NotEmpty(SchemaErrors(badAmount));
    }

    [Fact]
    public async Task Validation_RejectsWhatTheSdiWouldReject()
    {
        await ConfigureCompanyAsync();
        var customer = await CreateCustomerAsync(withFiscal: true);
        async Task<HttpStatusCode> Create(InvoiceLineRequest line) =>
            (await _admin.PostAsJsonAsync("/api/invoices", new SaveInvoiceRequest(customer.Id, "MP05", null, null, [line]))).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Create(new InvoiceLineRequest(null, "Servizio", 1, "pz", 100, 0, 21, null, null)));
        Assert.Equal(HttpStatusCode.BadRequest, await Create(new InvoiceLineRequest(null, "Servizio", 1, "pz", 100, 0, 0, null, null)));
        Assert.Equal(HttpStatusCode.BadRequest, await Create(new InvoiceLineRequest(null, " ", 1, "pz", 100, 0, 22, null, null)));
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/invoices", new SaveInvoiceRequest(
            customer.Id, "MP99", null, null, [new InvoiceLineRequest(null, "Servizio", 1, "pz", 100, 0, 22, null, null)]))).StatusCode);

        // A zero total can't be issued.
        var zero = (await (await _admin.PostAsJsonAsync("/api/invoices", new SaveInvoiceRequest(
            customer.Id, "MP05", null, null, [new InvoiceLineRequest(null, "Omaggio", 1, "pz", 0, 0, 22, null, null)])))
            .Content.ReadFromJsonAsync<InvoiceResponse>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync($"/api/invoices/{zero.Id}/issue", new IssueInvoiceRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/invoices/{zero.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"/api/customers/{customer.Id}/fiscal",
            new CustomerFiscalResponse(null, "TROPPOLUNGO", null, "Via", "24121", "Bergamo", "BG", "IT"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync("/api/company-profile/fiscal",
            new CompanyFiscalResponse(null, "RF01", "Via Po 1", "10123", "Torino", "TO", "IT", null, null, "IT60X0542811101000000123457"))).StatusCode);

        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "fatture-op");
        var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync("/api/invoices", new SaveInvoiceRequest(
            customer.Id, "MP05", null, null, [new InvoiceLineRequest(null, "Servizio", 1, "pz", 100, 0, 22, null, null)]))).StatusCode);
    }

    [Fact]
    public void Helpers_IbanProgressiveAndVat()
    {
        Assert.True(FiscalDataController.IsValidIban("IT60X0542811101000000123456"));
        Assert.True(FiscalDataController.IsValidIban("DE89370400440532013000"));
        Assert.False(FiscalDataController.IsValidIban("IT60X0542811101000000123457"));
        Assert.Equal(5, FatturaPa.Progressive(2026, 1).Length);
        Assert.NotEqual(FatturaPa.Progressive(2026, 1), FatturaPa.Progressive(2027, 1));
        Assert.Equal(5, FatturaPa.Progressive(2099, 99_999).Length);
        Assert.Equal("01234567890", FatturaPa.VatDigits("IT 012 345 678 90"));
        Assert.Equal("DE", FatturaPa.VatCountry("DE123456789", "IT"));
    }
}
