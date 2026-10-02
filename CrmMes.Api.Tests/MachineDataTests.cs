using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

/// <summary>Machine interconnection: token-authenticated readings and the figures computed from them.</summary>
public class MachineDataTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public MachineDataTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static MachineEvent At(DateTime from, int minute, string state, long? pieces = null) =>
        new() { Timestamp = from.AddMinutes(minute), State = state, PieceCounter = pieces };

    [Fact]
    public void Stats_SplitTheTimeByState_AndCountSilenceAsNoData()
    {
        var from = new DateTime(2026, 9, 30, 6, 0, 0, DateTimeKind.Utc);
        var events = new List<MachineEvent>
        {
            At(from, -30, "Off"),            // before the period: sets the initial state
            At(from, 0, "Running", 1000),
            At(from, 10, "Running", 1060),   // heartbeat
            At(from, 20, "Alarm"),
            At(from, 25, "Running", 1100),
            At(from, 35, "Idle"),
            At(from, 40, "Running", 1130),   // then silence: 15 min covered, the rest is unknown
        };

        var result = MachineStats.Compute(events, from, from.AddMinutes(100));
        Assert.Equal(20 + 10 + 15, result.MinutesByState["Running"]);
        Assert.Equal(5, result.MinutesByState["Alarm"]);
        Assert.Equal(5, result.MinutesByState["Idle"]);
        Assert.Equal(100 - 55, result.NoDataMinutes);
        Assert.Equal(130, result.Pieces);
        Assert.Equal(1, result.AlarmCount);
        Assert.Equal(Math.Round(45m / 55m, 4), result.Availability);
    }

    [Theory]
    [InlineData(new long[] { 100, 150, 200 }, 100L, 100L)]
    [InlineData(new long[] { 100, 180, 20, 50 }, 90L, 140L)]   // +10, +80, reset (20 from zero), +30
    [InlineData(new long[] { }, 10L, 0L)]
    public void CounterDelta_HandlesResets(long[] values, long start, long expected) =>
        Assert.Equal(expected, MachineStats.CounterDelta(start, values.Select(v => (long?)v)));

    [Fact]
    public async Task Machine_PushesReadingsWithItsToken_AndTheDayIsSummarised()
    {
        var equipment = (await (await _admin.PostAsJsonAsync("/api/equipment", new CreateEquipmentRequest($"Pressa {Guid.NewGuid():N}"[..14], $"PR-{Guid.NewGuid():N}"[..10], null)))
            .Content.ReadFromJsonAsync<EquipmentResponse>())!;
        var tokenResponse = await _admin.PostAsync($"/api/equipment/{equipment.Id}/machine-token", null);
        tokenResponse.EnsureSuccessStatusCode();
        var token = (await tokenResponse.Content.ReadFromJsonAsync<MachineTokenResponse>())!;
        Assert.Equal(MachineDataController.TokenHeader, token.Header);
        Assert.EndsWith($"/api/machine-data/{equipment.Id}", token.Endpoint);

        var anonymous = _fixture.Factory.CreateClient();
        HttpRequestMessage Push(string? key, params MachineReadingRequest[] readings)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/machine-data/{equipment.Id}") { Content = JsonContent.Create(readings.ToList()) };
            if (key is not null)
            {
                request.Headers.Add(MachineDataController.TokenHeader, key);
            }

            return request;
        }

        var now = DateTime.UtcNow;
        var start = now.AddMinutes(-30);
        MachineReadingRequest Reading(int minute, string state, long? pieces = null, string? alarm = null) =>
            new(start.AddMinutes(minute), state, pieces, null, alarm, alarm is null ? null : "Protezione aperta", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Push(null, Reading(0, "Running")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Push("token-sbagliato", Reading(0, "Running")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.SendAsync(Push(token.Token, Reading(0, "Acceso")))).StatusCode);

        var batch = await anonymous.SendAsync(Push(token.Token,
            Reading(0, "Running", 500), Reading(10, "Alarm", null, "E42"), Reading(12, "Running", 520), Reading(20, "Running", 580)));
        batch.EnsureSuccessStatusCode();
        Assert.Equal(4, (await batch.Content.ReadFromJsonAsync<MachineIngestResponse>())!.Accepted);

        var resend = await anonymous.SendAsync(Push(token.Token, Reading(20, "Running", 580), Reading(25, "Idle")));
        var resendResult = (await resend.Content.ReadFromJsonAsync<MachineIngestResponse>())!;
        Assert.Equal(1, resendResult.Accepted);
        Assert.Equal(1, resendResult.Duplicates);

        var day = (await _admin.GetFromJsonAsync<MachineDayResponse>($"/api/equipment/{equipment.Id}/machine-day"))!;
        Assert.True(day.Connected);
        Assert.Equal("Idle", day.LastState);
        Assert.Equal(80, day.Pieces);
        Assert.Equal(1, day.AlarmCount);
        Assert.Equal("E42", Assert.Single(day.Alarms).Code);
        Assert.True(day.MinutesByState["Running"] >= 17);

        var overview = (await _admin.GetFromJsonAsync<List<MachineOverviewResponse>>("/api/machine-data/overview"))!;
        Assert.Equal("Idle", overview.Single(o => o.EquipmentId == equipment.Id).LastState);

        // Only an Admin manages tokens; a new token revokes the old one; revoking cuts the machine off.
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "macchine-op");
        var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync($"/api/equipment/{equipment.Id}/machine-token", null)).StatusCode);

        var second = (await (await _admin.PostAsync($"/api/equipment/{equipment.Id}/machine-token", null)).Content.ReadFromJsonAsync<MachineTokenResponse>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Push(token.Token, Reading(27, "Running")))).StatusCode);
        (await anonymous.SendAsync(Push(second.Token, Reading(27, "Running")))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/equipment/{equipment.Id}/machine-token")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Push(second.Token, Reading(28, "Running")))).StatusCode);
    }

    [Fact]
    public async Task Dashboard_UsesMachineAvailability_WhenReadingsExistInPeriod()
    {
        var equipment = (await (await _admin.PostAsJsonAsync("/api/equipment", new CreateEquipmentRequest($"Tornio {Guid.NewGuid():N}"[..14], $"TN-{Guid.NewGuid():N}"[..10], null)))
            .Content.ReadFromJsonAsync<EquipmentResponse>())!;
        var token = (await (await _admin.PostAsync($"/api/equipment/{equipment.Id}/machine-token", null))
            .Content.ReadFromJsonAsync<MachineTokenResponse>())!;

        var anonymous = _fixture.Factory.CreateClient();
        var now = DateTime.UtcNow;
        var start = now.AddMinutes(-40);
        MachineReadingRequest Reading(int minute, string state, long? pieces = null, long? scrap = null) =>
            new(start.AddMinutes(minute), state, pieces, scrap, null, null, null);

        var push = new HttpRequestMessage(HttpMethod.Post, $"/api/machine-data/{equipment.Id}")
        {
            Content = JsonContent.Create(new List<MachineReadingRequest>
            {
                Reading(0, "Running", 1000, 10),
                Reading(20, "Idle", 1200, 10),
                Reading(30, "Running", 1300, 15),
            }),
        };
        push.Headers.Add(MachineDataController.TokenHeader, token.Token);
        (await anonymous.SendAsync(push)).EnsureSuccessStatusCode();

        var dashboard = (await _admin.GetFromJsonAsync<WorkOrderDashboardResponse>("/api/work-orders/dashboard?days=1"))!;
        Assert.True(dashboard.MachinesReportingInPeriod >= 1);
        Assert.NotNull(dashboard.MachineAvailabilityRatio);
        Assert.True(dashboard.OeeSource is "Machine" or "Hybrid");
        Assert.Equal(dashboard.MachineAvailabilityRatio, dashboard.AvailabilityRatio);
        Assert.NotNull(dashboard.MachineQualityRatio);
        // 300 pieces, 5 scrap → quality 300/305
        Assert.Equal(Math.Round(300m / 305m, 4), dashboard.MachineQualityRatio);
    }

    [Fact]
    public async Task DemoFeed_CreatesEvents_AndDashboardSeesMachineSource()
    {
        var equipment = (await (await _admin.PostAsJsonAsync("/api/equipment", new CreateEquipmentRequest($"Demo {Guid.NewGuid():N}"[..12], $"DM-{Guid.NewGuid():N}"[..10], null)))
            .Content.ReadFromJsonAsync<EquipmentResponse>())!;

        var feed = await _admin.PostAsync($"/api/equipment/{equipment.Id}/demo-feed?seconds=60", null);
        feed.EnsureSuccessStatusCode();
        var created = (await feed.Content.ReadFromJsonAsync<MachineDemoFeedResponse>())!;
        Assert.True(created.Created >= 10);

        var repeat = await (await _admin.PostAsync($"/api/equipment/{equipment.Id}/demo-feed?seconds=60", null)).Content
            .ReadFromJsonAsync<MachineDemoFeedResponse>();
        Assert.Equal(0, repeat!.Created);
        Assert.True(repeat.SkippedDuplicates >= 10);

        var dashboard = (await _admin.GetFromJsonAsync<WorkOrderDashboardResponse>("/api/work-orders/dashboard?days=1"))!;
        Assert.True(dashboard.MachinesReportingInPeriod >= 1);
        Assert.True(dashboard.OeeSource is "Machine" or "Hybrid");

        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "demo-op");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _fixture.Factory.AuthenticatedClient(operatorAuth.Token).PostAsync($"/api/equipment/{equipment.Id}/demo-feed", null)).StatusCode);
    }
}
