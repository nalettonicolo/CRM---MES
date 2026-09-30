using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

/// <summary>Remote assistance: contacts readable before login, server diagnostics for administrators only
/// and without secrets.</summary>
public class SupportTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;

    public SupportTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Info_IsReadableWithoutLogin_AndReportsTheServerVersion()
    {
        var info = await _fixture.Client.GetFromJsonAsync<SupportInfoResponse>("/api/support/info");

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info!.ServerVersion));
        Assert.Equal("altro", info.Hosting);
    }

    [Fact]
    public async Task Diagnostics_ForAdmin_ShowTheDatabaseState_WithoutSecrets()
    {
        var admin = _fixture.Factory.AuthenticatedClient(_fixture.Admin.Token);

        var response = await admin.GetAsync("/api/support/diagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var diagnostics = System.Text.Json.JsonSerializer.Deserialize<SupportDiagnosticsResponse>(
            body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.True(diagnostics.Database.CanConnect);
        Assert.True(diagnostics.Database.ActiveUsers >= 1);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("integration-test-signing-key", body);
    }

    [Fact]
    public async Task Diagnostics_AreDenied_ToAnyoneButAdmin()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fixture.Client.GetAsync("/api/support/diagnostics")).StatusCode);

        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/support/diagnostics")).StatusCode);
    }
}
