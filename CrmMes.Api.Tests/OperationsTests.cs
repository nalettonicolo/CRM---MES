using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

/// <summary>Everyday operations asked for from the floor: finding a job by the last digits of its code,
/// the list of open jobs, and the liveness endpoint that keeps the hosting awake.</summary>
public class OperationsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public OperationsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<WorkOrderResponse> CreateOrderAsync(string code, bool release)
    {
        var product = (await (await _adminClient.PostAsJsonAsync("/api/products", new CreateProductRequest($"P-{code}", $"Prodotto {code}", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        var response = await _adminClient.PostAsJsonAsync("/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, code, null, null, null, null));
        response.EnsureSuccessStatusCode();
        var order = (await response.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        if (release)
        {
            (await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release?force=true", null)).EnsureSuccessStatusCode();
        }

        return order;
    }

    [Fact]
    public async Task Lookup_FindsAJobByItsLastDigits_WithTheExactCodeFirst()
    {
        var stem = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var target = await CreateOrderAsync($"WO-{stem}-4711", release: true);
        await CreateOrderAsync($"WO-{stem}-14711", release: false);   // also ends with 4711
        await CreateOrderAsync($"WO-{stem}-4712", release: false);

        var bySuffix = (await _adminClient.GetFromJsonAsync<List<WorkOrderLookupResponse>>($"/api/work-orders/lookup?q=4711"))!
            .Where(r => r.Code.Contains(stem)).ToList();
        Assert.Equal(2, bySuffix.Count);
        Assert.Equal(target.Code, bySuffix[0].Code);                    // released job before the draft

        var exact = (await _adminClient.GetFromJsonAsync<List<WorkOrderLookupResponse>>($"/api/work-orders/lookup?q=WO-{stem}-14711"))!;
        Assert.Equal($"WO-{stem}-14711", exact[0].Code);

        var byLot = (await _adminClient.GetFromJsonAsync<List<WorkOrderLookupResponse>>($"/api/work-orders/lookup?q={target.ProductLotNumber}"))!;
        Assert.Contains(byLot, r => r.Id == target.Id);
        Assert.Equal($"Prodotto WO-{stem}-4711", bySuffix[0].ProductName);
    }

    [Fact]
    public async Task OpenJobs_AreReleasedOrInProgressOnly()
    {
        var stem = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var released = await CreateOrderAsync($"OP-{stem}-1", release: true);
        var draft = await CreateOrderAsync($"OP-{stem}-2", release: false);

        var open = (await _adminClient.GetFromJsonAsync<List<WorkOrderLookupResponse>>("/api/work-orders/lookup"))!;
        Assert.Contains(open, r => r.Id == released.Id);
        Assert.DoesNotContain(open, r => r.Id == draft.Id);

        var list = (await _adminClient.GetFromJsonAsync<List<WorkOrderSummaryResponse>>("/api/work-orders?status=Open"))!;
        Assert.Contains(list, r => r.Id == released.Id);
        Assert.DoesNotContain(list, r => r.Id == draft.Id);
    }

    [Fact]
    public async Task Ping_AnswersWithoutLoginAndWithoutTheDatabase()
    {
        var anonymous = _fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/work-orders/lookup")).StatusCode);
    }

    [Theory]
    [InlineData("2026-09-30T08:00:00Z", true)]    // Wednesday 10:00 in Italy
    [InlineData("2026-09-30T19:30:00Z", false)]   // Wednesday 21:30
    [InlineData("2026-10-04T09:00:00Z", false)]   // Sunday
    [InlineData("2026-10-03T04:30:00Z", true)]    // Saturday 6:30
    public void KeepWarm_WarmsTheDatabaseOnlyInWorkingHours(string utc, bool expected) =>
        Assert.Equal(expected, KeepWarmService.IsWorkingTime(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal)));
}
