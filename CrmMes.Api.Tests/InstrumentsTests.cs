using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class InstrumentsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _admin;
    private readonly AdminSeededApiTestFixture _fixture;

    public InstrumentsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    [Fact]
    public async Task CreateInstrument_AndRecordCalibration_UpdatesDueDates()
    {
        var code = $"STR-{Guid.NewGuid():N}"[..10];
        var create = await _admin.PostAsJsonAsync("/api/instruments", new CreateMeasuringInstrumentRequest(code, "Calibro", null, 180, null, null));
        create.EnsureSuccessStatusCode();
        var instrument = (await create.Content.ReadFromJsonAsync<MeasuringInstrumentResponse>())!;

        var cal = await _admin.PostAsJsonAsync($"/api/instruments/{instrument.Id}/calibrations",
            new RecordCalibrationRequest(DateTime.UtcNow, null, "Pass", "CERT-1", null));
        cal.EnsureSuccessStatusCode();

        var detail = await _admin.GetFromJsonAsync<MeasuringInstrumentDetailResponse>($"/api/instruments/{instrument.Id}");
        Assert.NotNull(detail!.LastCalibrationAt);
        Assert.NotNull(detail.NextCalibrationDue);
        Assert.Single(detail.Calibrations);
    }

    [Fact]
    public async Task Operator_CannotCreateInstrument()
    {
        var auth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "instr-op");
        using var client = _fixture.Factory.AuthenticatedClient(auth.Token);
        var response = await client.PostAsJsonAsync("/api/instruments", new CreateMeasuringInstrumentRequest("X1", "Test", null, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
