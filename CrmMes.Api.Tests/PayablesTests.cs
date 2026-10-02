using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

/// <summary>G8: passive FatturaPA import, payment schedule (payables + receivables), mark-paid and reminders.</summary>
public class PayablesTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public PayablesTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static string SamplePassiveXml(
        string number = "FT-100",
        string date = "2026-09-15",
        string vat = "09876543210",
        string due = "2026-10-15",
        string amount = "122.00") =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <FatturaElettronica xmlns="http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2" versione="FPR12">
          <FatturaElettronicaHeader>
            <CedentePrestatore>
              <DatiAnagrafici>
                <IdFiscaleIVA><IdPaese>IT</IdPaese><IdCodice>{vat}</IdCodice></IdFiscaleIVA>
                <Anagrafica><Denominazione>Fornitore Alfa spa</Denominazione></Anagrafica>
              </DatiAnagrafici>
            </CedentePrestatore>
            <CessionarioCommittente>
              <DatiAnagrafici>
                <IdFiscaleIVA><IdPaese>IT</IdPaese><IdCodice>01234567890</IdCodice></IdFiscaleIVA>
                <Anagrafica><Denominazione>Impianti Rossi srl</Denominazione></Anagrafica>
              </DatiAnagrafici>
            </CessionarioCommittente>
          </FatturaElettronicaHeader>
          <FatturaElettronicaBody>
            <DatiGenerali>
              <DatiGeneraliDocumento>
                <TipoDocumento>TD01</TipoDocumento>
                <Divisa>EUR</Divisa>
                <Data>{date}</Data>
                <Numero>{number}</Numero>
              </DatiGeneraliDocumento>
            </DatiGenerali>
            <DatiBeniServizi>
              <DettaglioLinee>
                <NumeroLinea>1</NumeroLinea>
                <Descrizione>Materiale elettrico</Descrizione>
                <Quantita>1.00</Quantita>
                <UnitaMisura>pz</UnitaMisura>
                <PrezzoUnitario>100.00</PrezzoUnitario>
                <PrezzoTotale>100.00</PrezzoTotale>
                <AliquotaIVA>22.00</AliquotaIVA>
              </DettaglioLinee>
              <DatiRiepilogo>
                <AliquotaIVA>22.00</AliquotaIVA>
                <ImponibileImporto>100.00</ImponibileImporto>
                <Imposta>22.00</Imposta>
              </DatiRiepilogo>
            </DatiBeniServizi>
            <DatiPagamento>
              <CondizioniPagamento>TP02</CondizioniPagamento>
              <DettaglioPagamento>
                <ModalitaPagamento>MP05</ModalitaPagamento>
                <DataScadenzaPagamento>{due}</DataScadenzaPagamento>
                <ImportoPagamento>{amount}</ImportoPagamento>
              </DettaglioPagamento>
            </DatiPagamento>
          </FatturaElettronicaBody>
        </FatturaElettronica>
        """;

    private async Task<PurchaseInvoiceDetailResponse> ImportAsync(string xml, string fileName = "fattura.xml")
    {
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(xml);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        content.Add(file, "file", fileName);
        var response = await _admin.PostAsync("/api/payables/purchase-invoices/import", content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return (await response.Content.ReadFromJsonAsync<PurchaseInvoiceDetailResponse>())!;
    }

    [Fact]
    public async Task Import_PassiveXml_CreatesInvoiceAndPayableSchedule()
    {
        var detail = await ImportAsync(SamplePassiveXml());

        Assert.Equal("FT-100", detail.DocumentNumber);
        Assert.Equal("Fornitore Alfa spa", detail.SupplierName);
        Assert.Equal("IT09876543210", detail.SupplierVat);
        Assert.Single(detail.Lines);
        Assert.Equal("Materiale elettrico", detail.Lines[0].Description);
        Assert.Single(detail.Schedule);
        Assert.Equal("Payable", detail.Schedule[0].Direction);
        Assert.Equal("Open", detail.Schedule[0].Status);
        Assert.Equal(122.00m, detail.Schedule[0].Amount);
        Assert.Equal(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), detail.Schedule[0].DueDate);
    }

    [Fact]
    public async Task Import_SameXmlTwice_ReturnsConflict()
    {
        var xml = SamplePassiveXml(number: "FT-DUP", vat: "11111111111");
        (await ImportAsync(xml)).Id.Should().NotBe(Guid.Empty);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        content.Add(file, "file", "dup.xml");
        var response = await _admin.PostAsync("/api/payables/purchase-invoices/import", content);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MarkPaid_And_Remind_UpdateScheduleEntry()
    {
        var detail = await ImportAsync(SamplePassiveXml(number: "FT-PAY", vat: "22222222222", due: "2026-01-01"));
        var entryId = detail.Schedule[0].Id;

        var reminded = await (await _admin.PostAsJsonAsync($"/api/payables/schedule/{entryId}/remind",
            new PayablesController.RemindRequest("Chiamato il fornitore"))).Content.ReadFromJsonAsync<ScheduleEntryResponse>();
        Assert.Equal(1, reminded!.ReminderCount);
        Assert.NotNull(reminded.RemindedAt);
        Assert.True(reminded.Overdue);

        var paid = await (await _admin.PostAsync($"/api/payables/schedule/{entryId}/mark-paid", null))
            .Content.ReadFromJsonAsync<ScheduleEntryResponse>();
        Assert.Equal("Paid", paid!.Status);
        Assert.NotNull(paid.PaidAt);

        var schedule = await _admin.GetFromJsonAsync<List<ScheduleEntryResponse>>("/api/payables/schedule?direction=Payable");
        Assert.Contains(schedule!, e => e.Id == entryId && e.Status == "Paid");
    }

    [Fact]
    public async Task Schedule_IncludesReceivables_FromIssuedSalesInvoices()
    {
        await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Impianti Rossi srl", "IT01234567890", "Via Po 1, Torino", null, null, "installations", null));
        (await _admin.PutAsJsonAsync("/api/company-profile/fiscal", new CompanyFiscalResponse(
            "01234567890", "RF01", "Via Po 1", "10123", "Torino", "to", "IT", "TO", "123456",
            "IT60 X054 2811 1010 0000 0123 456"))).EnsureSuccessStatusCode();

        var code = $"C-{Guid.NewGuid():N}"[..10];
        var customer = (await (await _admin.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest($"Cliente Scad {code}", code, "IT09876543210", null, null, "Via Roma 1", null)))
            .Content.ReadFromJsonAsync<CustomerResponse>())!;
        (await _admin.PutAsJsonAsync($"/api/customers/{customer.Id}/fiscal", new CustomerFiscalResponse(
            null, "M5UXCR1", null, "Via Roma 1", "20100", "Milano", "MI", "IT"))).EnsureSuccessStatusCode();

        var due = DateTime.UtcNow.Date.AddDays(20);
        var draft = await (await _admin.PostAsJsonAsync("/api/invoices", new
        {
            customerId = customer.Id,
            paymentMethod = "MP05",
            paymentDueDate = due,
            lines = new[]
            {
                new { description = "Servizio", quantity = 1m, unit = "pz", unitPrice = 100m, discountPercent = 0m, vatRate = 22m, vatNature = (string?)null }
            }
        })).Content.ReadFromJsonAsync<InvoiceResponse>();

        (await _admin.PostAsync($"/api/invoices/{draft!.Id}/issue", null)).EnsureSuccessStatusCode();

        var schedule = await _admin.GetFromJsonAsync<List<ScheduleEntryResponse>>("/api/payables/schedule?direction=Receivable&status=Open");
        Assert.Contains(schedule!, e =>
            e.Direction == "Receivable"
            && e.CounterpartyName == customer.Name
            && e.Amount == 122.00m
            && e.DueDate.Date == due);
    }

    [Fact]
    public async Task Import_InvalidXml_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("<not-a-fattura/>"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        content.Add(file, "file", "bad.xml");
        var response = await _admin.PostAsync("/api/payables/purchase-invoices/import", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

file static class PayablesTestAssert
{
    public static Guid Should(this Guid id) => id;
    public static Guid NotBe(this Guid id, Guid other)
    {
        Assert.NotEqual(other, id);
        return id;
    }
}
