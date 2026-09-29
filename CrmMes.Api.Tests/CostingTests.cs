using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class CostingTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public CostingTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private sealed record Scenario(WorkOrderResponse Order, string PricedCode, string UnpricedCode, Guid BenchCenterId);

    /// <summary>A 2-unit panel job: BOM 4 x priced material (catalog 12.40) + 1 x unpriced material;
    /// routing "Montaggio" 90 min on "Banco" (45 €/h) and "Collaudo" 30 min on a center with no rate.</summary>
    private async Task<Scenario> CreateScenarioAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var pricedCode = $"MP-{suffix}";
        var unpricedCode = $"MU-{suffix}";
        foreach (var code in new[] { pricedCode, unpricedCode })
        {
            (await _adminClient.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(code, $"Materiale {code}", "pz", 100, 0))).EnsureSuccessStatusCode();
        }

        var bench = (await (await _adminClient.PostAsJsonAsync("/api/work-centers", new CreateWorkCenterRequest($"BAN-{suffix}", $"Banco {suffix}", null, 480)))
            .Content.ReadFromJsonAsync<WorkCenterResponse>())!;
        (await _adminClient.PostAsJsonAsync("/api/work-centers", new CreateWorkCenterRequest($"COL-{suffix}", $"Collaudo {suffix}", null, 480))).EnsureSuccessStatusCode();
        (await _adminClient.PutAsJsonAsync($"/api/work-centers/{bench.Id}/hourly-rate", new SetHourlyRateRequest(45m))).EnsureSuccessStatusCode();

        var product = (await (await _adminClient.PostAsJsonAsync("/api/products", new CreateProductRequest($"QE-{suffix}", $"Quadro {suffix}", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        (await _adminClient.PutAsJsonAsync($"/api/products/{product.Id}/bom", new ReplaceBillOfMaterialRequest(
            [new BillOfMaterialItemRequest(pricedCode, 4, null), new BillOfMaterialItemRequest(unpricedCode, 1, null)]))).EnsureSuccessStatusCode();
        (await _adminClient.PutAsJsonAsync($"/api/products/{product.Id}/routing", new ReplaceRoutingRequest(
            [new RoutingStepRequest("Montaggio", null, $"Banco {suffix}", 90), new RoutingStepRequest("Collaudo", null, $"Collaudo {suffix}", 30)]))).EnsureSuccessStatusCode();

        var createOrder = await _adminClient.PostAsJsonAsync("/api/work-orders", new CreateWorkOrderRequest(product.Id, 2, null, null, null, null, null));
        createOrder.EnsureSuccessStatusCode();
        var order = (await createOrder.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Catalog: cheapest known price 12.40 for the priced material (a pricier supplier must be ignored).
        var pricedMaterial = await db.Materials.SingleAsync(m => m.Code == pricedCode);
        var cheap = new Supplier { Name = "Economico", Code = $"S1-{suffix}" };
        var pricey = new Supplier { Name = "Caro", Code = $"S2-{suffix}" };
        db.Suppliers.AddRange(cheap, pricey);
        db.MaterialSuppliers.AddRange(
            new MaterialSupplier { MaterialId = pricedMaterial.Id, SupplierId = cheap.Id, PartNumber = "A", UnitPrice = 12.40m },
            new MaterialSupplier { MaterialId = pricedMaterial.Id, SupplierId = pricey.Id, PartNumber = "B", UnitPrice = 19m });

        // Actual pick: 8 pcs of the priced material, 5 of them from a lot bought at 11.00, 3 with no lot.
        var purchaseOrder = new PurchaseOrder { Code = $"OD-{suffix}", SupplierId = cheap.Id, Status = "Received" };
        purchaseOrder.Items.Add(new PurchaseOrderItem { MaterialCode = pricedCode, Description = "x", Quantity = 5, UnitPrice = 11m, ReceivedQuantity = 5 });
        db.PurchaseOrders.Add(purchaseOrder);
        var lot = new MaterialLot { MaterialCode = pricedCode, LotNumber = $"L-{suffix}", Quantity = 0, InitialQuantity = 5, PurchaseOrderId = purchaseOrder.Id };
        db.MaterialLots.Add(lot);
        var area = new Area { Name = $"Reparto {suffix}", Code = $"AR-{suffix}" };
        db.Areas.Add(area);
        var admin = await db.Users.FirstAsync(u => u.Email == AdminSeededApiTestFixture.AdminEmail);
        var slip = new WithdrawalSlip { Code = $"DP-{suffix}", AreaId = area.Id, RequestedByUserId = admin.Id, WorkOrderId = order.Id, Status = "Closed" };
        var item = new WithdrawalItem { WithdrawalSlipId = slip.Id, MaterialCode = pricedCode, Description = "x", Quantity = 8 };
        slip.Items.Add(item);
        db.WithdrawalSlips.Add(slip);
        db.MaterialLotConsumptions.Add(new MaterialLotConsumption { MaterialLotId = lot.Id, WithdrawalItemId = item.Id, Quantity = 5 });

        // Actual time: "Montaggio" took exactly 60 minutes; "Collaudo" not started.
        var assembly = await db.WorkOrderOperations.SingleAsync(op => op.WorkOrderId == order.Id && op.Name == "Montaggio");
        assembly.StartedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        assembly.CompletedAt = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        assembly.Status = "Done";
        await db.SaveChangesAsync();

        return new Scenario(order, pricedCode, unpricedCode, bench.Id);
    }

    [Fact]
    public async Task Costing_ComputesEstimateActualAndMargin()
    {
        var scenario = await CreateScenarioAsync();
        (await _adminClient.PutAsJsonAsync($"/api/work-orders/{scenario.Order.Id}/sale-price", new SetSalePriceRequest(1000m))).EnsureSuccessStatusCode();
        (await _adminClient.PostAsJsonAsync($"/api/work-orders/{scenario.Order.Id}/labor",
            new AddLaborEntryRequest(30, DateTime.UtcNow, scenario.BenchCenterId, null, null, "Cablaggio fuori ciclo"))).EnsureSuccessStatusCode();

        var costing = (await _adminClient.GetFromJsonAsync<WorkOrderCostingResult>($"/api/work-orders/{scenario.Order.Id}/costing"))!;

        Assert.Equal(99.20m, costing.Estimated.Material);  // 4 x 2 x 12.40 (cheapest supplier)
        Assert.Equal(67.50m, costing.Estimated.Labor);     // 90 min at 45 €/h; Collaudo has no rate
        Assert.Equal(92.20m, costing.Actual.Material);     // 5 x 11.00 from the lot + 3 x 12.40 catalog
        Assert.Equal(67.50m, costing.Actual.Labor);        // 60 min phase + 30 min entry, at 45 €/h
        Assert.Equal(159.70m, costing.Actual.Total);
        Assert.Equal(840.30m, costing.ActualMargin!.Amount);
        Assert.Equal(0.8403m, costing.ActualMargin.Ratio);
        Assert.Equal(90m, costing.ActualMinutes);

        var material = Assert.Single(costing.Materials);
        Assert.Equal("Acquisto + listino", material.PriceSource);
        // Missing data is reported, never silently counted as zero.
        Assert.Contains(costing.Warnings, w => w.Contains("Collaudo"));
        Assert.Contains(costing.Warnings, w => w.Contains("distinta base senza prezzo"));
    }

    [Fact]
    public async Task Costing_PhaseOpenLongerThanAShift_IsFlagged()
    {
        var scenario = await CreateScenarioAsync();
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var testing = await db.WorkOrderOperations.SingleAsync(op => op.WorkOrderId == scenario.Order.Id && op.Name == "Collaudo");
            testing.StartedAt = DateTime.UtcNow.AddHours(-10); // left open overnight
            testing.Status = "InProgress";
            await db.SaveChangesAsync();
        }

        var costing = (await _adminClient.GetFromJsonAsync<WorkOrderCostingResult>($"/api/work-orders/{scenario.Order.Id}/costing"))!;

        Assert.Contains(costing.Warnings, w => w.StartsWith("1 fasi durano più di un turno"));
        Assert.Contains(costing.Labor, line => line.InProgress && line.Minutes >= 600);
    }

    [Fact]
    public async Task Costing_WithoutSalePrice_HasNoMarginAndWarns()
    {
        var scenario = await CreateScenarioAsync();

        var costing = (await _adminClient.GetFromJsonAsync<WorkOrderCostingResult>($"/api/work-orders/{scenario.Order.Id}/costing"))!;

        Assert.Null(costing.ActualMargin);
        Assert.Contains(costing.Warnings, w => w.Contains("Prezzo di vendita non impostato"));
    }

    [Fact]
    public async Task ConvertedQuote_SetsSalePriceFromQuoteLine()
    {
        var scenario = await CreateScenarioAsync();
        var code = $"C-{Guid.NewGuid():N}"[..12];
        var customer = (await (await _adminClient.PostAsJsonAsync("/api/customers", new SaveCustomerRequest("Cliente margine", code, null, null, null, null, null)))
            .Content.ReadFromJsonAsync<CustomerResponse>())!;
        var quote = (await (await _adminClient.PostAsJsonAsync("/api/quotes", new SaveQuoteRequest(customer.Id, null, null,
            [new QuoteItemRequest(scenario.Order.ProductId, "Quadro", 2, 500m, 10)]))).Content.ReadFromJsonAsync<QuoteResponse>())!;
        (await _adminClient.PostAsync($"/api/quotes/{quote.Id}/accept", null)).EnsureSuccessStatusCode();
        var converted = (await (await _adminClient.PostAsJsonAsync($"/api/quotes/{quote.Id}/convert", new ConvertQuoteRequest(null, null)))
            .Content.ReadFromJsonAsync<ConvertQuoteResponse>())!;

        var costing = (await _adminClient.GetFromJsonAsync<WorkOrderCostingResult>($"/api/work-orders/{converted.WorkOrders[0].Id}/costing"))!;

        Assert.Equal(900m, costing.SalePrice); // 2 x 500 with 10% off
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Sales")]
    [InlineData("Warehouse")]
    [InlineData("Purchasing")]
    public async Task Costs_AreForbiddenOutsideManagement(string role)
    {
        var scenario = await CreateScenarioAsync();
        var auth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, role, $"cost-{role.ToLowerInvariant()}");
        using var client = _fixture.Factory.AuthenticatedClient(auth.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/work-orders/{scenario.Order.Id}/costing")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/margins")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/work-centers/{scenario.BenchCenterId}/hourly-rate", new SetHourlyRateRequest(1m))).StatusCode);

        // Hourly rates are blanked in the work center list for them...
        var centers = (await client.GetFromJsonAsync<List<WorkCenterResponse>>("/api/work-centers"))!;
        Assert.All(centers, center => Assert.Null(center.HourlyRate));
        // ...while management does see them.
        var adminCenters = (await _adminClient.GetFromJsonAsync<List<WorkCenterResponse>>("/api/work-centers"))!;
        Assert.Equal(45m, adminCenters.Single(c => c.Id == scenario.BenchCenterId).HourlyRate);
    }

    [Fact]
    public async Task ManagementRole_SeesCostsAndMargins()
    {
        var scenario = await CreateScenarioAsync();
        (await _adminClient.PutAsJsonAsync($"/api/work-orders/{scenario.Order.Id}/sale-price", new SetSalePriceRequest(1000m))).EnsureSuccessStatusCode();
        var auth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Management", "direzione");
        using var client = _fixture.Factory.AuthenticatedClient(auth.Token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/work-orders/{scenario.Order.Id}/costing")).StatusCode);
        var overview = (await client.GetFromJsonAsync<MarginOverviewResponse>("/api/margins?take=200"))!;
        var row = Assert.Single(overview.WorkOrders, r => r.WorkOrderId == scenario.Order.Id);
        Assert.Equal(1000m, row.SalePrice);
    }

    [Fact]
    public async Task MarginOverview_BatchCosting_MatchesSingleCosting()
    {
        var first = await CreateScenarioAsync();
        var second = await CreateScenarioAsync();
        (await _adminClient.PutAsJsonAsync($"/api/work-orders/{first.Order.Id}/sale-price", new SetSalePriceRequest(1000m))).EnsureSuccessStatusCode();
        (await _adminClient.PutAsJsonAsync($"/api/work-orders/{second.Order.Id}/sale-price", new SetSalePriceRequest(100m))).EnsureSuccessStatusCode();
        (await _adminClient.PostAsJsonAsync($"/api/work-orders/{second.Order.Id}/labor",
            new AddLaborEntryRequest(120, null, second.BenchCenterId, null, null, null))).EnsureSuccessStatusCode();

        var overview = (await _adminClient.GetFromJsonAsync<MarginOverviewResponse>("/api/margins?take=200"))!;

        foreach (var scenario in new[] { first, second })
        {
            var single = (await _adminClient.GetFromJsonAsync<WorkOrderCostingResult>($"/api/work-orders/{scenario.Order.Id}/costing"))!;
            var row = Assert.Single(overview.WorkOrders, r => r.WorkOrderId == scenario.Order.Id);
            Assert.Equal(single.Actual.Total, row.ActualCost);
            Assert.Equal(single.Estimated.Total, row.EstimatedCost);
            Assert.Equal(single.ActualMargin?.Amount, row.ActualMargin);
        }

        // 100 € sale price against 92.20 materials + 135 € labor (60 + 120 min at 45 €/h): a loss.
        var loss = Assert.Single(overview.WorkOrders, r => r.WorkOrderId == second.Order.Id);
        Assert.True(loss.ActualMargin < 0);
        Assert.True(overview.LossMakingCount >= 1);
    }

    [Fact]
    public async Task Operator_CanLogHours_ButNotDeleteThem()
    {
        var scenario = await CreateScenarioAsync();
        var auth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "ore-op");
        using var client = _fixture.Factory.AuthenticatedClient(auth.Token);

        var add = await client.PostAsJsonAsync($"/api/work-orders/{scenario.Order.Id}/labor", new AddLaborEntryRequest(45, null, null, null, null, "Installazione"));
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var entry = (await add.Content.ReadFromJsonAsync<LaborEntryResponse>())!;
        Assert.Equal("Operator Tester", entry.OperatorName);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/work-orders/{scenario.Order.Id}/labor/{entry.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _adminClient.DeleteAsync($"/api/work-orders/{scenario.Order.Id}/labor/{entry.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(24 * 60 + 1)]
    public async Task LaborEntry_OutOfRangeMinutes_IsRejected(int minutes)
    {
        var scenario = await CreateScenarioAsync();

        var response = await _adminClient.PostAsJsonAsync($"/api/work-orders/{scenario.Order.Id}/labor", new AddLaborEntryRequest(minutes, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LaborEntry_WithOperationOfAnotherOrder_IsRejected()
    {
        var first = await CreateScenarioAsync();
        var second = await CreateScenarioAsync();

        var response = await _adminClient.PostAsJsonAsync($"/api/work-orders/{first.Order.Id}/labor",
            new AddLaborEntryRequest(30, null, null, second.Order.Operations[0].Id, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
