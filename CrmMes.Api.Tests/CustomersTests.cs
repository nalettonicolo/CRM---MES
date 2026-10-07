using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class CustomersTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public CustomersTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<CustomerResponse> CreateCustomerAsync()
    {
        var code = $"CLI-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/customers", new SaveCustomerRequest($"Cliente {code}", code, null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
    }

    [Fact]
    public async Task CreateCustomer_ThenList_ReturnsIt()
    {
        var customer = await CreateCustomerAsync();

        var listResponse = await _adminClient.GetFromJsonAsync<List<CustomerResponse>>("/api/customers");

        Assert.Contains(listResponse!, c => c.Id == customer.Id && c.Code == customer.Code);
    }

    [Fact]
    public async Task CreateCustomer_DuplicateCode_ReturnsConflict()
    {
        var code = $"DUP-{Guid.NewGuid():N}"[..12];
        await _adminClient.PostAsJsonAsync("/api/customers", new SaveCustomerRequest("Primo", code, null, null, null, null, null));

        var response = await _adminClient.PostAsJsonAsync("/api/customers", new SaveCustomerRequest("Secondo", code, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EditCustomer_UpdatesContactData()
    {
        var customer = await CreateCustomerAsync();

        var response = await _adminClient.PutAsJsonAsync(
            $"/api/customers/{customer.Id}",
            new SaveCustomerRequest("Rinominato", customer.Code, null, "nuovo@example.test", null, null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CustomerResponse>();
        Assert.Equal("Rinominato", updated!.Name);
        Assert.Equal("nuovo@example.test", updated.Email);
    }

    [Fact]
    public async Task CreateEditAndDeactivateCustomer_WriteAuditLog()
    {
        var customer = await CreateCustomerAsync();

        await _adminClient.PutAsJsonAsync(
            $"/api/customers/{customer.Id}",
            new SaveCustomerRequest("Rinominato", customer.Code, null, null, null, null, null));
        await _adminClient.DeleteAsync($"/api/customers/{customer.Id}");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmMes.Core.Data.ApplicationDbContext>();
        var actions = db.AuditLogs
            .Where(log => log.EntityType == "Customer" && log.EntityId == customer.Id)
            .Select(log => log.Action)
            .ToList();

        Assert.Contains("CustomerCreated", actions);
        Assert.Contains("CustomerUpdated", actions);
        Assert.Contains("CustomerDeactivated", actions);
    }
}
