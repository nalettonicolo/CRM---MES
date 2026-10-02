using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class AttendanceTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _admin;
    private readonly AdminSeededApiTestFixture _fixture;

    public AttendanceTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    [Fact]
    public async Task PunchToday_AppearsInTodayMe()
    {
        var punch = await _admin.PostAsJsonAsync("/api/attendance/punch", new PunchRequest(null, "In", null, "Test"));
        punch.EnsureSuccessStatusCode();

        var today = await _admin.GetFromJsonAsync<List<AttendancePunchResponse>>("/api/attendance/today/me");
        Assert.Contains(today!, p => p.Kind == "In" && p.Notes == "Test");
    }

    [Fact]
    public async Task Admin_CanQueryRange_OperatorCannot()
    {
        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var adminList = await _admin.GetAsync($"/api/attendance?from={from}&to={to}");
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);

        var auth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "att-op");
        using var client = _fixture.Factory.AuthenticatedClient(auth.Token);
        var denied = await client.GetAsync($"/api/attendance?from={from}&to={to}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
