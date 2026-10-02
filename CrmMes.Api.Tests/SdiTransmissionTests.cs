using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

/// <summary>G9: SdI transmission states and intermediary adapter (stub ready for a real provider).</summary>
public class SdiTransmissionTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _admin;
    private readonly AdminSeededApiTestFixture _fixture;

    public SdiTransmissionTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task ConfigureCompanyAsync()
    {
        (await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Impianti Rossi srl", "IT01234567890", "Via Po 1, Torino", null, null, "installations", null))).EnsureSuccessStatusCode();
        (await _admin.PutAsJsonAsync("/api/company-profile/fiscal", new CompanyFiscalResponse(
            "01234567890", "RF01", "Via Po 1", "10123", "Torino", "TO", "IT", null, null, "IT60X0542811101000000123456"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StatusTransitions_AndStubSubmit_WorkOnIssuedInvoice()
    {
        await ConfigureCompanyAsync();
        var customer = await (await _admin.PostAsJsonAsync("/api/customers", new
        {
            code = "SDI-C1",
            name = "Cliente SdI",
            vatNumber = "01234567890",
        })).Content.ReadFromJsonAsync<CustomerResponse>();

        await _admin.PutAsJsonAsync($"/api/customers/{customer!.Id}/fiscal", new
        {
            fiscalCode = "RSSMRA80A01H501U",
            sdiCode = "0000000",
            street = "Via Roma 1",
            postalCode = "20100",
            city = "Milano",
            province = "MI",
            country = "IT",
        });

        // Company fiscal needed for XML
        await _admin.PutAsJsonAsync("/api/company-profile/fiscal", new
        {
            fiscalCode = "01234567890",
            taxRegime = "RF01",
            street = "Via Officina 2",
            postalCode = "35100",
            city = "Padova",
            province = "PD",
            country = "IT",
            iban = "IT60X0542811101000000123456",
        });

        var draft = await (await _admin.PostAsJsonAsync("/api/invoices", new SaveInvoiceRequest(
            customer.Id, "MP05", DateTime.UtcNow.Date.AddDays(30), null,
            [new InvoiceLineRequest(null, "Servizio", 1, "pz", 100, 0, 22, null, null)])))
            .Content.ReadFromJsonAsync<InvoiceResponse>();

        Assert.Equal(SdiStatuses.NotSent, draft!.SdiStatus);

        var issued = await (await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/issue", new IssueInvoiceRequest(null)))
            .Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal("Issued", issued!.Status);
        Assert.Equal(SdiStatuses.NotSent, issued.SdiStatus);

        var submitted = await (await _admin.PostAsync($"/api/invoices/{draft.Id}/sdi/submit", null))
            .Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal(SdiStatuses.Submitted, submitted!.SdiStatus);
        Assert.StartsWith("STUB-", submitted.SdiTransmissionId);

        var accepted = await (await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/sdi/status",
            new RecordSdiStatusRequest(SdiStatuses.Accepted, submitted.SdiTransmissionId, "Ricevuta dal destinatario")))
            .Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal(SdiStatuses.Accepted, accepted!.SdiStatus);

        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/sdi/status",
                new RecordSdiStatusRequest(SdiStatuses.Rejected, null, null))).StatusCode);
    }

    [Fact]
    public async Task ManualStatus_FromNotSent_ToSubmitted_WithoutCallingProviderAgain()
    {
        await ConfigureCompanyAsync();
        var customer = await (await _admin.PostAsJsonAsync("/api/customers", new
        {
            code = "SDI-C2",
            name = "Cliente manuale",
            vatNumber = "09876543211",
        })).Content.ReadFromJsonAsync<CustomerResponse>();

        await _admin.PutAsJsonAsync($"/api/customers/{customer!.Id}/fiscal", new
        {
            sdiCode = "ABC1234",
            street = "Via Test 3",
            postalCode = "00100",
            city = "Roma",
            province = "RM",
            country = "IT",
        });

        var draft = await (await _admin.PostAsJsonAsync("/api/invoices", new SaveInvoiceRequest(
            customer.Id, "MP05", null, null,
            [new InvoiceLineRequest(null, "Voce", 1, "pz", 50, 0, 22, null, null)])))
            .Content.ReadFromJsonAsync<InvoiceResponse>();
        (await _admin.PostAsJsonAsync($"/api/invoices/{draft!.Id}/issue", new IssueInvoiceRequest(null))).EnsureSuccessStatusCode();

        var marked = await (await _admin.PostAsJsonAsync($"/api/invoices/{draft.Id}/sdi/status",
            new RecordSdiStatusRequest(SdiStatuses.Submitted, "MANUAL-1", "Caricato a mano sul portale")))
            .Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal(SdiStatuses.Submitted, marked!.SdiStatus);
        Assert.Equal("MANUAL-1", marked.SdiTransmissionId);
    }
}
