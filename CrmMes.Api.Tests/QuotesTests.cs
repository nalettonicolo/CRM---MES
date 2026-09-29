using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class QuotesTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public QuotesTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<CustomerResponse> CreateCustomerAsync(HttpClient? client = null)
    {
        var code = $"CLI-{Guid.NewGuid():N}"[..12];
        var response = await (client ?? _adminClient).PostAsJsonAsync(
            "/api/customers",
            new SaveCustomerRequest($"Cliente {code}", code, "IT01234567890", "ordini@example.invalid", null, "Via Roma 1, Torino", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
    }

    private async Task<MaterialResponse> CreateMaterialAsync()
    {
        var code = $"MAT-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/materials", new CreateMaterialRequest(code, $"Materiale {code}", "pz", 100, 0));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MaterialResponse>())!;
    }

    /// <summary>A product with a 2-line bill of materials and a 2-step routing, like a small panel.</summary>
    private async Task<(ProductResponse Product, MaterialResponse PricedMaterial, MaterialResponse UnpricedMaterial)> CreatePanelProductAsync()
    {
        var code = $"QE-{Guid.NewGuid():N}"[..12];
        var productResponse = await _adminClient.PostAsJsonAsync("/api/products", new CreateProductRequest(code, $"Quadro {code}", null));
        productResponse.EnsureSuccessStatusCode();
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var priced = await CreateMaterialAsync();
        var unpriced = await CreateMaterialAsync();
        (await _adminClient.PutAsJsonAsync($"/api/products/{product.Id}/bom", new ReplaceBillOfMaterialRequest(
        [
            new BillOfMaterialItemRequest(priced.Code, 4, null),
            new BillOfMaterialItemRequest(unpriced.Code, 1, null)
        ]))).EnsureSuccessStatusCode();
        (await _adminClient.PutAsJsonAsync($"/api/products/{product.Id}/routing", new ReplaceRoutingRequest(
        [
            new RoutingStepRequest("Montaggio", null, "Banco 1", 90),
            new RoutingStepRequest("Collaudo", null, "Collaudo", 30)
        ]))).EnsureSuccessStatusCode();

        return (product, priced, unpriced);
    }

    private async Task<QuoteResponse> CreateQuoteAsync(Guid customerId, Guid? productId, decimal quantity = 2)
    {
        var items = new List<QuoteItemRequest>
        {
            new(productId, "Quadro di comando macchina", quantity, 1250.50m, 10),
            new(null, "Trasporto e installazione", 1, 300m)
        };
        var response = await _adminClient.PostAsJsonAsync("/api/quotes", new SaveQuoteRequest(customerId, DateTime.UtcNow.AddDays(30), "Consegna in 6 settimane", items));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuoteResponse>())!;
    }

    [Fact]
    public async Task CreateCustomer_DuplicateCode_ReturnsConflict()
    {
        var customer = await CreateCustomerAsync();

        var response = await _adminClient.PostAsJsonAsync(
            "/api/customers", new SaveCustomerRequest("Altro", customer.Code, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SearchCustomers_IsCaseInsensitive()
    {
        var customer = await CreateCustomerAsync();

        var results = await _adminClient.GetFromJsonAsync<List<CustomerResponse>>($"/api/customers?q={customer.Code.ToLowerInvariant()}");

        Assert.Contains(results!, c => c.Id == customer.Id);
    }

    [Fact]
    public async Task CreateCustomer_AsOperator_ReturnsForbidden()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "cli-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PostAsJsonAsync(
            "/api/customers", new SaveCustomerRequest("Non autorizzato", $"OP-{Guid.NewGuid():N}"[..12], null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SalesRole_CanManageCustomersAndQuotes()
    {
        var salesAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Sales", "sales");
        using var salesClient = _fixture.Factory.AuthenticatedClient(salesAuth.Token);

        var customer = await CreateCustomerAsync(salesClient);
        var response = await salesClient.PostAsJsonAsync("/api/quotes", new SaveQuoteRequest(
            customer.Id, null, null, [new QuoteItemRequest(null, "Consulenza", 1, 100m)]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_ComputesLineAndQuoteTotals()
    {
        var customer = await CreateCustomerAsync();
        var (product, _, _) = await CreatePanelProductAsync();

        var quote = await CreateQuoteAsync(customer.Id, product.Id);

        Assert.Equal("Draft", quote.Status);
        Assert.StartsWith("PV-", quote.Code);
        // 2 x 1250.50 with 10% off = 2250.90; plus 300 = 2550.90
        Assert.Equal(2250.90m, quote.Items[0].LineTotal);
        Assert.Equal(2550.90m, quote.Total);
        Assert.Equal(product.Code, quote.Items[0].ProductCode);
    }

    [Fact]
    public async Task CreateQuote_WithoutLines_ReturnsBadRequest()
    {
        var customer = await CreateCustomerAsync();

        var response = await _adminClient.PostAsJsonAsync("/api/quotes", new SaveQuoteRequest(customer.Id, null, null, []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EditQuote_ReplacesLines_OnlyWhileDraft()
    {
        var customer = await CreateCustomerAsync();
        var quote = await CreateQuoteAsync(customer.Id, null);

        var edit = await _adminClient.PutAsJsonAsync($"/api/quotes/{quote.Id}", new SaveQuoteRequest(
            customer.Id, null, "Rivisto", [new QuoteItemRequest(null, "Riga unica", 3, 10m)]));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = (await edit.Content.ReadFromJsonAsync<QuoteResponse>())!;
        Assert.Single(edited.Items);
        Assert.Equal(30m, edited.Total);

        (await _adminClient.PostAsync($"/api/quotes/{quote.Id}/send", null)).EnsureSuccessStatusCode();
        var editAfterSend = await _adminClient.PutAsJsonAsync($"/api/quotes/{quote.Id}", new SaveQuoteRequest(
            customer.Id, null, null, [new QuoteItemRequest(null, "Tardi", 1, 1m)]));
        Assert.Equal(HttpStatusCode.Conflict, editAfterSend.StatusCode);
    }

    [Fact]
    public async Task RejectedQuote_CannotBeAccepted()
    {
        var customer = await CreateCustomerAsync();
        var quote = await CreateQuoteAsync(customer.Id, null);

        (await _adminClient.PostAsync($"/api/quotes/{quote.Id}/reject", null)).EnsureSuccessStatusCode();
        var accept = await _adminClient.PostAsync($"/api/quotes/{quote.Id}/accept", null);

        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
    }

    [Fact]
    public async Task ConvertQuote_BeforeAcceptance_ReturnsConflict()
    {
        var customer = await CreateCustomerAsync();
        var (product, _, _) = await CreatePanelProductAsync();
        var quote = await CreateQuoteAsync(customer.Id, product.Id);

        var convert = await _adminClient.PostAsJsonAsync($"/api/quotes/{quote.Id}/convert", new ConvertQuoteRequest(null, null));

        Assert.Equal(HttpStatusCode.Conflict, convert.StatusCode);
    }

    [Fact]
    public async Task ConvertAcceptedQuote_CreatesWorkOrderPerProductLine_LinkedToCustomerAndQuote()
    {
        var customer = await CreateCustomerAsync();
        var (product, _, _) = await CreatePanelProductAsync();
        var quote = await CreateQuoteAsync(customer.Id, product.Id, quantity: 3);
        (await _adminClient.PostAsync($"/api/quotes/{quote.Id}/send", null)).EnsureSuccessStatusCode();
        (await _adminClient.PostAsync($"/api/quotes/{quote.Id}/accept", null)).EnsureSuccessStatusCode();

        var dueDate = DateTime.UtcNow.Date.AddDays(42);
        var convert = await _adminClient.PostAsJsonAsync($"/api/quotes/{quote.Id}/convert", new ConvertQuoteRequest(dueDate, null));

        Assert.Equal(HttpStatusCode.OK, convert.StatusCode);
        var result = (await convert.Content.ReadFromJsonAsync<ConvertQuoteResponse>())!;
        // The free-text "Trasporto" line is priced but not produced: only one work order.
        var created = Assert.Single(result.WorkOrders);
        Assert.Equal(3m, created.Quantity);

        var order = (await _adminClient.GetFromJsonAsync<WorkOrderResponse>($"/api/work-orders/{created.Id}"))!;
        Assert.Equal(customer.Id, order.CustomerId);
        Assert.Equal(quote.Id, order.QuoteId);
        Assert.Equal(dueDate, order.DueDate);
        Assert.Equal(2, order.Operations.Count); // routing snapshotted like a hand-made work order
        Assert.Equal("Draft", order.Status);

        var detail = (await _adminClient.GetFromJsonAsync<CustomerDetailResponse>($"/api/customers/{customer.Id}/detail"))!;
        Assert.Contains(detail.WorkOrders, w => w.Id == created.Id);
        Assert.Contains(detail.Quotes, q => q.Id == quote.Id);
    }

    [Fact]
    public async Task ConvertQuote_Twice_ReturnsConflict()
    {
        var customer = await CreateCustomerAsync();
        var (product, _, _) = await CreatePanelProductAsync();
        var quote = await CreateQuoteAsync(customer.Id, product.Id);
        (await _adminClient.PostAsync($"/api/quotes/{quote.Id}/accept", null)).EnsureSuccessStatusCode();
        (await _adminClient.PostAsJsonAsync($"/api/quotes/{quote.Id}/convert", new ConvertQuoteRequest(null, null))).EnsureSuccessStatusCode();

        var second = await _adminClient.PostAsJsonAsync($"/api/quotes/{quote.Id}/convert", new ConvertQuoteRequest(null, null));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task ProductMaterialCost_UsesCheapestSupplierPrice_AndCountsMissingPrices()
    {
        var (product, priced, _) = await CreatePanelProductAsync();

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cheap = new Supplier { Name = "Fornitore economico", Code = $"S-{Guid.NewGuid():N}"[..12] };
            var expensive = new Supplier { Name = "Fornitore caro", Code = $"S-{Guid.NewGuid():N}"[..12] };
            db.Suppliers.AddRange(cheap, expensive);
            db.MaterialSuppliers.AddRange(
                new MaterialSupplier { MaterialId = priced.Id, SupplierId = cheap.Id, PartNumber = "A1", UnitPrice = 12.40m },
                new MaterialSupplier { MaterialId = priced.Id, SupplierId = expensive.Id, PartNumber = "B1", UnitPrice = 15m });
            await db.SaveChangesAsync();
        }

        var cost = (await _adminClient.GetFromJsonAsync<ProductMaterialCostResponse>($"/api/products/{product.Id}/material-cost"))!;

        Assert.Equal(49.60m, cost.MaterialCost); // 4 x 12.40, cheapest supplier
        Assert.Equal(1, cost.MissingPriceCount);
    }
}
