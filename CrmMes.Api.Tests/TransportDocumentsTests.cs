using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class TransportDocumentsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public TransportDocumentsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static SaveTransportDocumentRequest SaleRequest(params SaveTransportDocumentLineRequest[] lines) => new(
        "Sale", null, null, null, "Cliente Prova spa", "Via Garibaldi 3, Milano", "IT09876543210", null,
        "Sender", null, "Franco", "Scatole", 2, 18.5m, null, null, null, null,
        lines.Length > 0 ? [.. lines] : [new SaveTransportDocumentLineRequest(null, null, "ART-1", "Staffa zincata", 10, "pz", null, null)]);

    private async Task<TransportDocumentResponse> CreateAsync(SaveTransportDocumentRequest request)
    {
        var response = await _adminClient.PostAsJsonAsync("/api/transport-documents", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
    }

    private async Task<TransportDocumentResponse> IssueAsync(Guid id)
    {
        var response = await _adminClient.PostAsJsonAsync($"/api/transport-documents/{id}/issue", new IssueTransportDocumentRequest(null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
    }

    private async Task<SupplierResponse> CreateSupplierAsync()
    {
        var code = $"TER-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest($"Zincatura {code}", code, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SupplierResponse>())!;
    }

    [Fact]
    public async Task Draft_IsEditable_ThenIssuingAssignsConsecutiveNumbersAndFreezesIt()
    {
        var draft = await CreateAsync(SaleRequest());
        Assert.Equal("Draft", draft.Status);
        Assert.Null(draft.Number);
        Assert.Equal("Bozza", draft.DocumentCode);
        Assert.Equal("Vendita", draft.ReasonLabel);

        var edited = await _adminClient.PutAsJsonAsync($"/api/transport-documents/{draft.Id}", SaleRequest(
            new SaveTransportDocumentLineRequest(null, null, "ART-1", "Staffa zincata", 12, "pz", "L-77", null),
            new SaveTransportDocumentLineRequest(null, null, null, "Viteria assortita", 1, "kg", null, null)));
        edited.EnsureSuccessStatusCode();
        var editedDocument = (await edited.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
        Assert.Equal([1, 2], editedDocument.Lines.Select(line => line.LineNumber));
        Assert.Equal(12, editedDocument.Lines[0].Quantity);

        var first = await IssueAsync(draft.Id);
        var second = await IssueAsync((await CreateAsync(SaleRequest())).Id);
        Assert.Equal("Issued", first.Status);
        Assert.NotNull(first.IssuedAt);
        Assert.NotNull(first.TransportStartAt);
        Assert.Equal(first.Year, second.Year);
        Assert.Equal(first.Number + 1, second.Number);
        Assert.Equal($"{first.Number}/{first.Year}", first.DocumentCode);

        // Issued: no edit, no delete, no second issue.
        Assert.Equal(HttpStatusCode.Conflict, (await _adminClient.PutAsJsonAsync($"/api/transport-documents/{first.Id}", SaleRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _adminClient.DeleteAsync($"/api/transport-documents/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _adminClient.PostAsJsonAsync($"/api/transport-documents/{first.Id}/issue", new IssueTransportDocumentRequest(null))).StatusCode);
    }

    [Fact]
    public async Task Cancel_NeedsAReason_AndKeepsTheNumber_WhileDraftsAreDeleted()
    {
        var issued = await IssueAsync((await CreateAsync(SaleRequest())).Id);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _adminClient.PostAsJsonAsync($"/api/transport-documents/{issued.Id}/cancel", new CancelTransportDocumentRequest(" "))).StatusCode);

        var cancelResponse = await _adminClient.PostAsJsonAsync($"/api/transport-documents/{issued.Id}/cancel", new CancelTransportDocumentRequest("Destinatario errato"));
        cancelResponse.EnsureSuccessStatusCode();
        var cancelled = (await cancelResponse.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(issued.Number, cancelled.Number);

        // The next document does not reuse the cancelled number.
        var next = await IssueAsync((await CreateAsync(SaleRequest())).Id);
        Assert.True(next.Number > issued.Number);

        var draft = await CreateAsync(SaleRequest());
        Assert.Equal(HttpStatusCode.NoContent, (await _adminClient.DeleteAsync($"/api/transport-documents/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _adminClient.GetAsync($"/api/transport-documents/{draft.Id}")).StatusCode);
    }

    public static TheoryData<SaveTransportDocumentRequest> InvalidRequests => new()
    {
        SaleRequest() with { Reason = "Regalo" },
        SaleRequest() with { Reason = "Other", ReasonDetail = null },
        SaleRequest() with { Reason = "Subcontracting" },                       // no subcontractor
        SaleRequest() with { TransportBy = "Carrier", CarrierId = null },       // no carrier
        SaleRequest() with { Port = "Gratis" },
        SaleRequest() with { RecipientName = " " },
        SaleRequest() with { Packages = -1 },
        SaleRequest() with { Lines = [] },
        SaleRequest(new SaveTransportDocumentLineRequest(null, null, null, "Pezzo", 0, "pz", null, null)),
        SaleRequest(new SaveTransportDocumentLineRequest(null, null, null, " ", 1, "pz", null, null)),
        SaleRequest(new SaveTransportDocumentLineRequest(Guid.NewGuid(), null, null, "Pezzo", 1, "pz", null, null)),
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_RejectsIncompleteDocuments(SaveTransportDocumentRequest request)
    {
        var response = await _adminClient.PostAsJsonAsync("/api/transport-documents", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FromWorkOrder_PrefillsCustomerProductLotAndQuantity()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = (await (await _adminClient.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest($"Cliente {suffix}", $"C-{suffix}", "IT11122233344", null, null, "Corso Francia 9, Torino", null)))
            .Content.ReadFromJsonAsync<CustomerResponse>())!;
        var product = (await (await _adminClient.PostAsJsonAsync("/api/products", new CreateProductRequest($"P-{suffix}", $"Prodotto {suffix}", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        var orderResponse = await _adminClient.PostAsJsonAsync("/api/work-orders", new CreateWorkOrderRequest(product.Id, 3, null, null, null, null, null, CustomerId: customer.Id));
        orderResponse.EnsureSuccessStatusCode();
        var order = (await orderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var response = await _adminClient.PostAsync($"/api/transport-documents/from-work-order/{order.Id}", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = (await response.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
        Assert.Equal("Draft", document.Status);
        Assert.Equal(customer.Name, document.RecipientName);
        Assert.Equal("Corso Francia 9, Torino", document.RecipientAddress);
        Assert.Equal(order.Code, document.WorkOrderCode);
        var line = Assert.Single(document.Lines);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(order.ProductLotNumber, line.LotNumber);
    }

    [Fact]
    public async Task Subcontracting_TracksReturnsAndScrap_UntilNothingIsLeftAtTheSubcontractor()
    {
        var supplier = await CreateSupplierAsync();
        var draft = await CreateAsync(SaleRequest(
            new SaveTransportDocumentLineRequest(null, null, "TEL-01", "Telaio da zincare", 10, "pz", null, null)) with
        {
            Reason = "Subcontracting", SupplierId = supplier.Id, RecipientName = null,
            ExpectedReturnAt = DateTime.UtcNow.Date.AddDays(-2)
        });
        Assert.Equal(supplier.Name, draft.RecipientName);
        Assert.Equal("Conto lavorazione", draft.ReasonLabel);

        // No returns on a draft.
        var lineId = draft.Lines[0].Id;
        Assert.Equal(HttpStatusCode.Conflict, (await AddReturnAsync(draft.Id, lineId, 1, 0)).StatusCode);

        var issued = await IssueAsync(draft.Id);
        lineId = issued.Lines[0].Id;
        (await AddReturnAsync(issued.Id, lineId, 6, 1, "DDT terzista 45")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await AddReturnAsync(issued.Id, lineId, 4, 0)).StatusCode); // only 3 left
        Assert.Equal(HttpStatusCode.BadRequest, (await AddReturnAsync(issued.Id, lineId, 0, 0)).StatusCode);

        var open = await _adminClient.GetFromJsonAsync<List<SubcontractingOpenLineResponse>>($"/api/subcontracting/open?supplierId={supplier.Id}");
        var row = Assert.Single(open!);
        Assert.Equal(10, row.SentQuantity);
        Assert.Equal(6, row.ReturnedQuantity);
        Assert.Equal(1, row.ScrapQuantity);
        Assert.Equal(3, row.OutstandingQuantity);
        Assert.True(row.IsOverdue);

        // With returns recorded the document can't be cancelled.
        Assert.Equal(HttpStatusCode.Conflict,
            (await _adminClient.PostAsJsonAsync($"/api/transport-documents/{issued.Id}/cancel", new CancelTransportDocumentRequest("errore"))).StatusCode);

        var last = await AddReturnAsync(issued.Id, lineId, 3, 0);
        last.EnsureSuccessStatusCode();
        var closed = (await last.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
        Assert.Equal(0, closed.Lines[0].OutstandingQuantity);
        Assert.Equal(2, closed.Lines[0].Returns.Count);
        Assert.Empty((await _adminClient.GetFromJsonAsync<List<SubcontractingOpenLineResponse>>($"/api/subcontracting/open?supplierId={supplier.Id}"))!);

        // Deleting a return reopens the balance.
        var deleted = await _adminClient.DeleteAsync($"/api/transport-documents/{issued.Id}/returns/{closed.Lines[0].Returns[1].Id}");
        deleted.EnsureSuccessStatusCode();
        Assert.Equal(3, Assert.Single((await _adminClient.GetFromJsonAsync<List<SubcontractingOpenLineResponse>>($"/api/subcontracting/open?supplierId={supplier.Id}"))!).OutstandingQuantity);
    }

    [Fact]
    public async Task Issue_WithCatalogMaterial_DeductsStock_AndCancelRestoresIt()
    {
        var code = $"MAT-{Guid.NewGuid():N}"[..12];
        var created = await _adminClient.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(code, $"Staffa {code}", "pz", 40, 0));
        created.EnsureSuccessStatusCode();
        var material = (await created.Content.ReadFromJsonAsync<MaterialResponse>())!;

        var draft = await CreateAsync(SaleRequest(
            new SaveTransportDocumentLineRequest(material.Id, null, material.Code, material.Name, 12, "pz", null, null)));
        var issued = await IssueAsync(draft.Id);
        Assert.Equal("Issued", issued.Status);

        var afterIssue = (await _adminClient.GetFromJsonAsync<MaterialResponse>($"/api/materials/{material.Id}"))!;
        Assert.Equal(28, afterIssue.Stock);

        var cancel = await _adminClient.PostAsJsonAsync($"/api/transport-documents/{issued.Id}/cancel",
            new CancelTransportDocumentRequest("sbagliato destinatario"));
        cancel.EnsureSuccessStatusCode();
        var afterCancel = (await _adminClient.GetFromJsonAsync<MaterialResponse>($"/api/materials/{material.Id}"))!;
        Assert.Equal(40, afterCancel.Stock);
    }

    [Fact]
    public async Task Operator_CanReadButNotWriteTransportDocuments()
    {
        var auth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "ddt-op");
        var client = _fixture.Factory.AuthenticatedClient(auth.Token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/transport-documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/transport-documents", SaleRequest())).StatusCode);
    }

    private Task<HttpResponseMessage> AddReturnAsync(Guid documentId, Guid lineId, decimal quantity, decimal scrap, string? reference = null) =>
        _adminClient.PostAsJsonAsync($"/api/transport-documents/{documentId}/lines/{lineId}/returns",
            new AddSubcontractingReturnRequest(quantity, scrap, null, reference, null));
}
