using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class ServiceTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public ServiceTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<CustomerResponse> CustomerAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest($"Cliente {suffix}", $"CLI-{suffix}", null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
    }

    private async Task<InstalledMachineResponse> MachineAsync(Guid customerId, DateTime? warrantyUntil = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _admin.PostAsJsonAsync("/api/service/machines",
            new CreateInstalledMachineRequest(customerId, null, $"Quadro {suffix}", "QE-100", $"SN-{suffix}", "Capannone 2", DateTime.UtcNow.AddYears(-1), warrantyUntil, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InstalledMachineResponse>())!;
    }

    [Fact]
    public async Task CreatingAMachine_RejectsDuplicateSerialNumber_AndUnknownCustomer()
    {
        var customer = await CustomerAsync();
        var machine = await MachineAsync(customer.Id);

        var duplicate = await _admin.PostAsJsonAsync("/api/service/machines",
            new CreateInstalledMachineRequest(customer.Id, null, "Altra macchina", null, machine.SerialNumber, null, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var unknownCustomer = await _admin.PostAsJsonAsync("/api/service/machines",
            new CreateInstalledMachineRequest(Guid.NewGuid(), null, "X", null, $"SN-{Guid.NewGuid():N}", null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, unknownCustomer.StatusCode);
    }

    [Fact]
    public async Task OperatorCannotCreateMachinesOrRequests_ButCanRecordAnIntervention()
    {
        var customer = await CustomerAsync();
        var machine = await MachineAsync(customer.Id);
        var operator_ = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var client = _fixture.Factory.AuthenticatedClient(operator_.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/service/machines",
            new CreateInstalledMachineRequest(customer.Id, null, "X", null, $"SN-{Guid.NewGuid():N}", null, null, null, null))).StatusCode);

        var createRequest = await _admin.PostAsJsonAsync("/api/service/requests",
            new CreateServiceRequestRequest(machine.Id, "Allarme termico", "Il quadro va in allarme dopo 2 ore", "Urgente", "Phone", "Sig. Rossi", "333-1234567"));
        createRequest.EnsureSuccessStatusCode();
        var request = (await createRequest.Content.ReadFromJsonAsync<ServiceRequestDetailResponse>())!;
        Assert.Equal("Open", request.Status);
        Assert.True(request.Number > 0);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/service/requests",
            new CreateServiceRequestRequest(machine.Id, "X", null, null, null, null, null))).StatusCode);

        // The technician (any authenticated role) records the visit; the request moves to InProgress.
        var intervention = await client.PostAsJsonAsync($"/api/service/requests/{request.Id}/interventions",
            new CreateServiceInterventionRequest("Mario Bianchi", null, true, 2.5m, true, "Sostituito il termostato", "Termostato TS-10", null));
        Assert.Equal(HttpStatusCode.Created, intervention.StatusCode);
        var updated = (await intervention.Content.ReadFromJsonAsync<ServiceRequestDetailResponse>())!;
        Assert.Equal("InProgress", updated.Status);
        var visit = Assert.Single(updated.Interventions);
        Assert.True(visit.InWarranty);
        Assert.Equal(2.5m, visit.Hours);
    }

    [Fact]
    public async Task ClosingAndReopeningARequest_FollowsItsInterventions()
    {
        var customer = await CustomerAsync();
        var machine = await MachineAsync(customer.Id);
        var created = (await (await _admin.PostAsJsonAsync("/api/service/requests",
            new CreateServiceRequestRequest(machine.Id, "Rumore anomalo", null, null, null, null, null)))
            .Content.ReadFromJsonAsync<ServiceRequestDetailResponse>())!;

        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync($"/api/service/requests/{created.Id}/reopen", null)).StatusCode);

        var closed = (await (await _admin.PostAsync($"/api/service/requests/{created.Id}/close", null))
            .Content.ReadFromJsonAsync<ServiceRequestDetailResponse>())!;
        Assert.Equal("Closed", closed.Status);
        Assert.NotNull(closed.ClosedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync($"/api/service/requests/{created.Id}/close", null)).StatusCode);

        // No interventions yet: reopening goes back to Open, not InProgress.
        var reopened = (await (await _admin.PostAsync($"/api/service/requests/{created.Id}/reopen", null))
            .Content.ReadFromJsonAsync<ServiceRequestDetailResponse>())!;
        Assert.Equal("Open", reopened.Status);
        Assert.Null(reopened.ClosedAt);

        // Adding an intervention to a closed request is refused: reopen it first.
        (await _admin.PostAsync($"/api/service/requests/{created.Id}/close", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsJsonAsync($"/api/service/requests/{created.Id}/interventions",
            new CreateServiceInterventionRequest(null, null, true, 1, false, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task MachineDetail_ListsItsOwnRequests_AndMachineListShowsOpenCount()
    {
        var customer = await CustomerAsync();
        var machine = await MachineAsync(customer.Id, warrantyUntil: DateTime.UtcNow.AddMonths(6));
        (await _admin.PostAsJsonAsync("/api/service/requests", new CreateServiceRequestRequest(machine.Id, "Richiesta 1", null, null, null, null, null))).EnsureSuccessStatusCode();
        var secondCreate = await _admin.PostAsJsonAsync("/api/service/requests", new CreateServiceRequestRequest(machine.Id, "Richiesta 2", null, null, null, null, null));
        var second = (await secondCreate.Content.ReadFromJsonAsync<ServiceRequestDetailResponse>())!;
        await _admin.PostAsync($"/api/service/requests/{second.Id}/close", null);

        var detail = await _admin.GetFromJsonAsync<InstalledMachineDetailResponse>($"/api/service/machines/{machine.Id}");
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Requests.Count);

        var list = await _admin.GetFromJsonAsync<List<InstalledMachineResponse>>("/api/service/machines");
        Assert.Contains(list!, m => m.Id == machine.Id && m.OpenRequestCount == 1);

        var byText = await _admin.GetFromJsonAsync<List<InstalledMachineResponse>>($"/api/service/machines?q={machine.SerialNumber}");
        Assert.Contains(byText!, m => m.Id == machine.Id);
    }

    [Fact]
    public async Task DecommissionedMachine_IsHiddenFromTheActiveList()
    {
        var customer = await CustomerAsync();
        var machine = await MachineAsync(customer.Id);

        (await _admin.PostAsync($"/api/service/machines/{machine.Id}/decommission", null)).EnsureSuccessStatusCode();

        var activeOnly = await _admin.GetFromJsonAsync<List<InstalledMachineResponse>>("/api/service/machines");
        Assert.DoesNotContain(activeOnly!, m => m.Id == machine.Id);
        var all = await _admin.GetFromJsonAsync<List<InstalledMachineResponse>>("/api/service/machines?activeOnly=false");
        Assert.Contains(all!, m => m.Id == machine.Id && m.Status == "Decommissioned");
    }
}
