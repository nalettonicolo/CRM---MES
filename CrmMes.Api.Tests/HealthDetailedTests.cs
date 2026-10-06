using System.Net;
using System.Text.Json;

namespace CrmMes.Api.Tests;

public class HealthDetailedTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public HealthDetailedTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HealthDetailed_IsAnonymousAndReportsPostgresCheck()
    {
        var response = await _fixture.Client.GetAsync("/health/detailed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("Healthy", root.GetProperty("status").GetString());

        var checks = root.GetProperty("checks").EnumerateArray().ToList();
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "postgres"
            && check.GetProperty("status").GetString() == "Healthy");
    }
}
