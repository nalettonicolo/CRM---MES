using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class MaintenanceTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public MaintenanceTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<EquipmentResponse> CreateEquipmentAsync()
    {
        var code = $"EQ-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/equipment", new CreateEquipmentRequest($"Macchina {code}", code, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentResponse>())!;
    }

    [Fact]
    public async Task CreateEquipment_ThenList_ReturnsIt()
    {
        var equipment = await CreateEquipmentAsync();

        var listResponse = await _adminClient.GetFromJsonAsync<List<EquipmentResponse>>("/api/equipment");

        Assert.Contains(listResponse!, e => e.Id == equipment.Id);
    }

    [Fact]
    public async Task CreateEquipment_DuplicateCode_ReturnsConflict()
    {
        var code = $"DUP-{Guid.NewGuid():N}"[..12];
        await _adminClient.PostAsJsonAsync("/api/equipment", new CreateEquipmentRequest("Primo", code, null));

        var response = await _adminClient.PostAsJsonAsync("/api/equipment", new CreateEquipmentRequest("Secondo", code, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreatePreventiveTask_ThenComplete_AppearsInEquipmentDetailAndSpawnsNextOccurrence()
    {
        var equipment = await CreateEquipmentAsync();

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/maintenance-tasks",
            new CreateMaintenanceTaskRequest(equipment.Id, "Lubrificazione", null, "Preventiva", DateTime.UtcNow.AddDays(1), 30));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var task = (await createResponse.Content.ReadFromJsonAsync<MaintenanceTaskResponse>())!;

        var completeResponse = await _adminClient.PostAsJsonAsync(
            $"/api/maintenance-tasks/{task.Id}/complete", new CompleteMaintenanceTaskRequest(_fixture.Admin.UserId, "Fatto"));
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var completed = await completeResponse.Content.ReadFromJsonAsync<MaintenanceTaskResponse>();
        Assert.Equal("Completed", completed!.Status);

        var pendingTasks = await _adminClient.GetFromJsonAsync<List<MaintenanceTaskResponse>>(
            $"/api/maintenance-tasks?equipmentId={equipment.Id}&status=Pending");
        Assert.Contains(pendingTasks!, t => t.Title == "Lubrificazione" && t.Id != task.Id);

        var detailResponse = await _adminClient.GetAsync($"/api/equipment/{equipment.Id}/detail");
        var detail = await detailResponse.Content.ReadFromJsonAsync<EquipmentDetailResponse>();
        Assert.Equal(2, detail!.Tasks.Count);
    }

    [Fact]
    public async Task CompleteTask_Twice_ReturnsConflict()
    {
        var equipment = await CreateEquipmentAsync();
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/maintenance-tasks",
            new CreateMaintenanceTaskRequest(equipment.Id, "Guasto motore", "Sostituito cuscinetto", "Correttiva", null, null));
        var task = (await createResponse.Content.ReadFromJsonAsync<MaintenanceTaskResponse>())!;

        await _adminClient.PostAsJsonAsync($"/api/maintenance-tasks/{task.Id}/complete", new CompleteMaintenanceTaskRequest(null, null));
        var secondResponse = await _adminClient.PostAsJsonAsync($"/api/maintenance-tasks/{task.Id}/complete", new CompleteMaintenanceTaskRequest(null, null));

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task CreateTask_InvalidType_ReturnsBadRequest()
    {
        var equipment = await CreateEquipmentAsync();

        var response = await _adminClient.PostAsJsonAsync(
            "/api/maintenance-tasks",
            new CreateMaintenanceTaskRequest(equipment.Id, "Titolo", null, "Sbagliato", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
