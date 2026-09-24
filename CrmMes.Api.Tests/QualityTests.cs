using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class QualityTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public QualityTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<(ProductResponse Product, WorkOrderResponse WorkOrder)> CreateProductWithWorkOrderAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products", new CreateProductRequest($"PROD-{suffix}", "Prodotto test qualità", null));
        productResponse.EnsureSuccessStatusCode();
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var workOrderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        workOrderResponse.EnsureSuccessStatusCode();
        var workOrder = (await workOrderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        return (product, workOrder);
    }

    [Fact]
    public async Task CreateCheckpoint_ThenListByProduct_ReturnsIt()
    {
        var (product, _) = await CreateProductWithWorkOrderAsync();

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/quality-checkpoints",
            new CreateQualityCheckpointRequest(product.Id, "Diametro foro", "mm", 10m, 9.8m, 10.2m));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var checkpoint = (await createResponse.Content.ReadFromJsonAsync<QualityCheckpointResponse>())!;

        var listResponse = await _adminClient.GetFromJsonAsync<List<QualityCheckpointResponse>>($"/api/quality-checkpoints?productId={product.Id}");
        Assert.Contains(listResponse!, c => c.Id == checkpoint.Id);
    }

    [Fact]
    public async Task CreateCheckpoint_LowerGreaterThanUpper_ReturnsBadRequest()
    {
        var (product, _) = await CreateProductWithWorkOrderAsync();

        var response = await _adminClient.PostAsJsonAsync(
            "/api/quality-checkpoints",
            new CreateQualityCheckpointRequest(product.Id, "Invalido", null, null, 10m, 5m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RecordMeasurement_WithinTolerance_PassesAndAppearsInCertificate()
    {
        var (product, workOrder) = await CreateProductWithWorkOrderAsync();
        var checkpointResponse = await _adminClient.PostAsJsonAsync(
            "/api/quality-checkpoints",
            new CreateQualityCheckpointRequest(product.Id, "Peso", "kg", 5m, 4.9m, 5.1m));
        var checkpoint = (await checkpointResponse.Content.ReadFromJsonAsync<QualityCheckpointResponse>())!;

        var measurementResponse = await _adminClient.PostAsJsonAsync(
            "/api/quality-measurements",
            new CreateQualityMeasurementRequest(checkpoint.Id, workOrder.Id, null, 5.0m, _fixture.Admin.UserId, null));
        Assert.Equal(HttpStatusCode.Created, measurementResponse.StatusCode);
        var measurement = await measurementResponse.Content.ReadFromJsonAsync<QualityMeasurementResponse>();
        Assert.True(measurement!.IsWithinTolerance);

        var certificateResponse = await _adminClient.GetAsync($"/api/quality-measurements/certificate/{workOrder.Id}");
        var certificate = await certificateResponse.Content.ReadFromJsonAsync<QualityCertificateResponse>();
        Assert.True(certificate!.AllPassed);
        Assert.Single(certificate.Measurements);
    }

    [Fact]
    public async Task RecordMeasurement_OutOfTolerance_FailsCertificate()
    {
        var (product, workOrder) = await CreateProductWithWorkOrderAsync();
        var checkpointResponse = await _adminClient.PostAsJsonAsync(
            "/api/quality-checkpoints",
            new CreateQualityCheckpointRequest(product.Id, "Lunghezza", "mm", 100m, 99m, 101m));
        var checkpoint = (await checkpointResponse.Content.ReadFromJsonAsync<QualityCheckpointResponse>())!;

        var measurementResponse = await _adminClient.PostAsJsonAsync(
            "/api/quality-measurements",
            new CreateQualityMeasurementRequest(checkpoint.Id, workOrder.Id, null, 150m, null, "Fuori tolleranza evidente"));
        var measurement = await measurementResponse.Content.ReadFromJsonAsync<QualityMeasurementResponse>();
        Assert.False(measurement!.IsWithinTolerance);

        var certificateResponse = await _adminClient.GetAsync($"/api/quality-measurements/certificate/{workOrder.Id}");
        var certificate = await certificateResponse.Content.ReadFromJsonAsync<QualityCertificateResponse>();
        Assert.False(certificate!.AllPassed);
    }

    [Fact]
    public async Task RecordMeasurement_CheckpointFromAnotherProduct_ReturnsBadRequest()
    {
        var (_, workOrder) = await CreateProductWithWorkOrderAsync();
        var (otherProduct, _) = await CreateProductWithWorkOrderAsync();
        var checkpointResponse = await _adminClient.PostAsJsonAsync(
            "/api/quality-checkpoints",
            new CreateQualityCheckpointRequest(otherProduct.Id, "Di un altro prodotto", null, null, null, null));
        var checkpoint = (await checkpointResponse.Content.ReadFromJsonAsync<QualityCheckpointResponse>())!;

        var response = await _adminClient.PostAsJsonAsync(
            "/api/quality-measurements",
            new CreateQualityMeasurementRequest(checkpoint.Id, workOrder.Id, null, 1m, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
