using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class EnergyTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public EnergyTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<EquipmentResponse> EquipmentAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _admin.PostAsJsonAsync("/api/equipment", new CreateEquipmentRequest($"Forno {suffix}", $"FR-{suffix}", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentResponse>())!;
    }

    /// <summary>Inserts readings directly in the database (bypassing the machine token), the way a real
    /// gateway's history would already sit there by the time someone opens a report.</summary>
    private async Task SeedReadingsAsync(Guid equipmentId, params (DateTime At, decimal Kwh)[] readings)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmMes.Core.Data.ApplicationDbContext>();
        foreach (var (at, kwh) in readings)
        {
            db.MachineEvents.Add(new MachineEvent { EquipmentId = equipmentId, Timestamp = at, State = "Running", EnergyKwh = kwh });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Consumption_SumsPositiveDeltas_AndIgnoresAMeterReset()
    {
        var equipment = await EquipmentAsync();
        var day = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedReadingsAsync(equipment.Id,
            (day.AddHours(6), 1000m),
            (day.AddHours(12), 1080m),   // +80
            (day.AddHours(18), 50m),     // meter reset: ignored, not a negative consumption
            (day.AddHours(22), 90m));    // +40 from the reset point

        var response = await _admin.GetFromJsonAsync<EnergyConsumptionResponse>(
            $"/api/energy/equipment/{equipment.Id}/consumption?from={Uri.EscapeDataString(day.ToString("o"))}&to={Uri.EscapeDataString(day.AddDays(1).ToString("o"))}");

        Assert.NotNull(response);
        Assert.Equal(120m, response!.TotalKwh);
        Assert.Equal(4, response.ReadingCount);
    }

    [Fact]
    public async Task Consumption_WithNoReadings_IsNullNotZero()
    {
        var equipment = await EquipmentAsync();

        var response = await _admin.GetFromJsonAsync<EnergyConsumptionResponse>(
            $"/api/energy/equipment/{equipment.Id}/consumption?from=2026-01-01&to=2026-01-31");

        Assert.NotNull(response);
        Assert.Null(response!.TotalKwh);
        Assert.Equal(0, response.ReadingCount);
    }

    [Fact]
    public async Task DailyConsumption_GroupsByDay()
    {
        var equipment = await EquipmentAsync();
        var day1 = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        await SeedReadingsAsync(equipment.Id,
            (day1.AddHours(1), 0m),
            (day1.AddHours(23), 50m),       // day 1: +50
            (day1.AddDays(1).AddHours(1), 90m)); // day 2: +40

        var daily = await _admin.GetFromJsonAsync<List<DailyEnergyResponse>>($"/api/energy/equipment/{equipment.Id}/daily?days=30");

        Assert.NotNull(daily);
        Assert.Equal(2, daily!.Count);
        Assert.Equal(50m, daily.Single(d => d.Day == day1.Date).Kwh);
        Assert.Equal(40m, daily.Single(d => d.Day == day1.Date.AddDays(1)).Kwh);
    }

    [Fact]
    public async Task EfficiencyProject_ComparesBaselineAndAfter_NormalisedByDay()
    {
        var equipment = await EquipmentAsync();
        var baselineFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var baselineTo = baselineFrom.AddDays(10); // 10 days
        var afterFrom = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var afterTo = afterFrom.AddDays(20); // 20 days, must be normalised against the 10-day baseline

        // Baseline: 1000 kWh over 10 days = 100 kWh/day.
        await SeedReadingsAsync(equipment.Id, (baselineFrom.AddHours(1), 0m), (baselineTo.AddHours(-1), 1000m));
        // After: 1200 kWh over 20 days = 60 kWh/day → 40% saved vs the baseline's 100 kWh/day.
        await SeedReadingsAsync(equipment.Id, (afterFrom.AddHours(1), 0m), (afterTo.AddHours(-1), 1200m));

        var create = await _admin.PostAsJsonAsync("/api/energy/projects",
            new CreateEnergyProjectRequest(equipment.Id, "Sostituzione forno con pompa di calore", "Progetto Industria 5.0", baselineFrom, baselineTo));
        create.EnsureSuccessStatusCode();
        var project = (await create.Content.ReadFromJsonAsync<EnergyProjectResponse>())!;
        Assert.Equal(1000m, project.BaselineKwh);
        Assert.Null(project.AfterKwh);
        Assert.Null(project.SavingsPercent);

        var withAfter = await _admin.PutAsJsonAsync($"/api/energy/projects/{project.Id}/after-period", new SetAfterPeriodRequest(afterFrom, afterTo));
        withAfter.EnsureSuccessStatusCode();
        var updated = (await withAfter.Content.ReadFromJsonAsync<EnergyProjectResponse>())!;

        Assert.Equal(1200m, updated.AfterKwh);
        Assert.Equal(40.0m, updated.SavingsPercent);
    }

    [Fact]
    public async Task CreatingAProject_ValidatesTitleAndPeriod_AndOperatorCannotManage()
    {
        var equipment = await EquipmentAsync();
        var from = DateTime.UtcNow.AddDays(-10);

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/energy/projects",
            new CreateEnergyProjectRequest(equipment.Id, "", null, from, from.AddDays(5)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/energy/projects",
            new CreateEnergyProjectRequest(equipment.Id, "Progetto", null, from, from))).StatusCode);

        var operator_ = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var client = _fixture.Factory.AuthenticatedClient(operator_.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/energy/projects",
            new CreateEnergyProjectRequest(equipment.Id, "Progetto", null, from, from.AddDays(5)))).StatusCode);

        // Reading figures stay open to everyone authenticated.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/energy/equipment/{equipment.Id}/consumption?from=2026-01-01&to=2026-01-31")).StatusCode);
    }

    [Fact]
    public async Task WorkOrderConsumption_AttributesTheIntervalToTheCodeActiveAtItsStart_AcrossMachines()
    {
        var pressa = await EquipmentAsync();
        var forno = await EquipmentAsync();
        var code = $"WO-{Guid.NewGuid():N}"[..12];
        var day = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmMes.Core.Data.ApplicationDbContext>();
            // Pressa: running WO-code from 0 to 100 kWh (+100 credited to this job), then another job for +20 (not credited).
            db.MachineEvents.Add(new MachineEvent { EquipmentId = pressa.Id, Timestamp = day.AddHours(1), State = "Running", EnergyKwh = 0, WorkOrderCode = code });
            db.MachineEvents.Add(new MachineEvent { EquipmentId = pressa.Id, Timestamp = day.AddHours(5), State = "Running", EnergyKwh = 100, WorkOrderCode = "ALTRA-COMMESSA" });
            db.MachineEvents.Add(new MachineEvent { EquipmentId = pressa.Id, Timestamp = day.AddHours(9), State = "Running", EnergyKwh = 120, WorkOrderCode = null });
            // Forno: a later phase of the same job, +30 kWh.
            db.MachineEvents.Add(new MachineEvent { EquipmentId = forno.Id, Timestamp = day.AddHours(2), State = "Running", EnergyKwh = 500, WorkOrderCode = code });
            db.MachineEvents.Add(new MachineEvent { EquipmentId = forno.Id, Timestamp = day.AddHours(6), State = "Running", EnergyKwh = 530, WorkOrderCode = code });
            await db.SaveChangesAsync();
        }

        var response = await _admin.GetFromJsonAsync<WorkOrderEnergyResponse>($"/api/energy/work-orders/{code}/consumption");

        Assert.NotNull(response);
        Assert.Equal(130m, response!.TotalKwh); // 100 (pressa) + 30 (forno)
        Assert.Equal(2, response.ByEquipment.Count);
        Assert.Equal(100m, response.ByEquipment.Single(e => e.EquipmentId == pressa.Id).Kwh);
        Assert.Equal(30m, response.ByEquipment.Single(e => e.EquipmentId == forno.Id).Kwh);
    }

    [Fact]
    public async Task WorkOrderConsumption_WithNoMatchingReadings_IsNull()
    {
        var response = await _admin.GetFromJsonAsync<WorkOrderEnergyResponse>($"/api/energy/work-orders/NESSUNA-{Guid.NewGuid():N}/consumption");

        Assert.NotNull(response);
        Assert.Null(response!.TotalKwh);
        Assert.Empty(response.ByEquipment);
    }

    [Fact]
    public async Task DeletingAProject_RemovesItFromTheList()
    {
        var equipment = await EquipmentAsync();
        var from = DateTime.UtcNow.AddDays(-10);
        var project = (await (await _admin.PostAsJsonAsync("/api/energy/projects",
            new CreateEnergyProjectRequest(equipment.Id, "Da cancellare", null, from, from.AddDays(5))))
            .Content.ReadFromJsonAsync<EnergyProjectResponse>())!;

        (await _admin.DeleteAsync($"/api/energy/projects/{project.Id}")).EnsureSuccessStatusCode();

        var list = await _admin.GetFromJsonAsync<List<EnergyProjectResponse>>($"/api/energy/projects?equipmentId={equipment.Id}");
        Assert.DoesNotContain(list!, p => p.Id == project.Id);
    }
}
