using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class ShipmentsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public ShipmentsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<CarrierResponse> CreateCarrierAsync()
    {
        var code = $"CAR-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/carriers", new CreateCarrierRequest($"Corriere {code}", code, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CarrierResponse>())!;
    }

    private async Task<Guid> CreatePurchaseOrderAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var supplierResponse = await _adminClient.PostAsJsonAsync(
            "/api/suppliers", new CreateSupplierRequest($"Fornitore {suffix}", $"SUP-{suffix}", null, null));
        supplierResponse.EnsureSuccessStatusCode();
        var supplier = (await supplierResponse.Content.ReadFromJsonAsync<SupplierResponse>())!;

        var materialResponse = await _adminClient.PostAsJsonAsync(
            "/api/materials", new CreateMaterialRequest($"MAT-{suffix}", "Materiale test", "pz", 0, 0));
        materialResponse.EnsureSuccessStatusCode();
        var material = (await materialResponse.Content.ReadFromJsonAsync<MaterialResponse>())!;

        var poResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null, [new CreatePurchaseOrderItemRequest(material.Code, 10, null, 1, null)]));
        poResponse.EnsureSuccessStatusCode();
        var doc = await poResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return doc.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateWorkOrderAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products", new CreateProductRequest($"PROD-{suffix}", "Prodotto test", null));
        productResponse.EnsureSuccessStatusCode();
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var woResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        woResponse.EnsureSuccessStatusCode();
        return (await woResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!.Id;
    }

    [Fact]
    public async Task CreateShipment_Inbound_LinkedToPurchaseOrder_Succeeds()
    {
        var carrier = await CreateCarrierAsync();
        var purchaseOrderId = await CreatePurchaseOrderAsync();

        var response = await _adminClient.PostAsJsonAsync(
            "/api/shipments",
            new CreateShipmentRequest("Inbound", carrier.Id, "TRK123", purchaseOrderId, null, "Fornitore ACME", null, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var shipment = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal("Inbound", shipment!.Direction);
        Assert.Equal("Preparing", shipment.Status);
        Assert.Equal(purchaseOrderId, shipment.PurchaseOrderId);
        Assert.Equal(carrier.Name, shipment.CarrierName);
    }

    [Fact]
    public async Task CreateShipment_Outbound_LinkedToWorkOrder_Succeeds()
    {
        var carrier = await CreateCarrierAsync();
        var workOrderId = await CreateWorkOrderAsync();

        var response = await _adminClient.PostAsJsonAsync(
            "/api/shipments",
            new CreateShipmentRequest("Outbound", carrier.Id, null, null, workOrderId, "Cliente Rossi", "Via Roma 1", null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var shipment = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal("Outbound", shipment!.Direction);
        Assert.Equal(workOrderId, shipment.WorkOrderId);
    }

    [Fact]
    public async Task CreateShipment_InboundWithWorkOrderId_ReturnsBadRequest()
    {
        var carrier = await CreateCarrierAsync();
        var workOrderId = await CreateWorkOrderAsync();

        var response = await _adminClient.PostAsJsonAsync(
            "/api/shipments",
            new CreateShipmentRequest("Inbound", carrier.Id, null, null, workOrderId, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateShipment_InvalidDirection_ReturnsBadRequest()
    {
        var carrier = await CreateCarrierAsync();

        var response = await _adminClient.PostAsJsonAsync(
            "/api/shipments",
            new CreateShipmentRequest("Sideways", carrier.Id, null, null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShipThenDeliverShipment_UpdatesStatusAndTimestamps()
    {
        var carrier = await CreateCarrierAsync();
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/shipments",
            new CreateShipmentRequest("Outbound", carrier.Id, null, null, null, "Cliente Test", null, null, null));
        var shipment = (await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>())!;

        var shipResponse = await _adminClient.PostAsJsonAsync($"/api/shipments/{shipment.Id}/ship", new ShipShipmentRequest("TRK999"));
        Assert.Equal(HttpStatusCode.OK, shipResponse.StatusCode);
        var shipped = (await shipResponse.Content.ReadFromJsonAsync<ShipmentResponse>())!;
        Assert.Equal("Shipped", shipped.Status);
        Assert.NotNull(shipped.ShippedAt);
        Assert.Equal("TRK999", shipped.TrackingNumber);

        var deliverResponse = await _adminClient.PostAsync($"/api/shipments/{shipment.Id}/deliver", null);
        Assert.Equal(HttpStatusCode.OK, deliverResponse.StatusCode);
        var delivered = (await deliverResponse.Content.ReadFromJsonAsync<ShipmentResponse>())!;
        Assert.Equal("Delivered", delivered.Status);
        Assert.NotNull(delivered.DeliveredAt);
    }

    [Fact]
    public async Task DeliverShipment_WhenStillPreparing_ReturnsConflict()
    {
        var carrier = await CreateCarrierAsync();
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/shipments",
            new CreateShipmentRequest("Outbound", carrier.Id, null, null, null, null, null, null, null));
        var shipment = (await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>())!;

        var response = await _adminClient.PostAsync($"/api/shipments/{shipment.Id}/deliver", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CancelShipment_FromPreparing_Succeeds()
    {
        var carrier = await CreateCarrierAsync();
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/shipments",
            new CreateShipmentRequest("Inbound", carrier.Id, null, null, null, null, null, null, null));
        var shipment = (await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>())!;

        var response = await _adminClient.PostAsync($"/api/shipments/{shipment.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal("Cancelled", cancelled!.Status);
    }

    [Fact]
    public async Task CancelShipment_WhenAlreadyDelivered_ReturnsConflict()
    {
        var carrier = await CreateCarrierAsync();
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/shipments",
            new CreateShipmentRequest("Outbound", carrier.Id, null, null, null, null, null, null, null));
        var shipment = (await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>())!;
        await _adminClient.PostAsJsonAsync($"/api/shipments/{shipment.Id}/ship", new ShipShipmentRequest(null));
        await _adminClient.PostAsync($"/api/shipments/{shipment.Id}/deliver", null);

        var response = await _adminClient.PostAsync($"/api/shipments/{shipment.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetShipments_FilterByDirection_ReturnsOnlyMatching()
    {
        var carrier = await CreateCarrierAsync();
        await _adminClient.PostAsJsonAsync(
            "/api/shipments", new CreateShipmentRequest("Inbound", carrier.Id, null, null, null, "Test filtro in", null, null, null));
        await _adminClient.PostAsJsonAsync(
            "/api/shipments", new CreateShipmentRequest("Outbound", carrier.Id, null, null, null, "Test filtro out", null, null, null));

        var inbound = await _adminClient.GetFromJsonAsync<List<ShipmentSummaryResponse>>("/api/shipments?direction=Inbound");

        Assert.All(inbound!, s => Assert.Equal("Inbound", s.Direction));
        Assert.Contains(inbound!, s => s.CounterpartReference == "Test filtro in");
    }
}
