using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

public class AssistantTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;

    public AssistantTests(AdminSeededApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Ask_Stub_ReturnsItalianModuleHint()
    {
        using var authed = _fixture.Factory.AuthenticatedClient(_fixture.Admin.Token);
        var response = await authed.PostAsJsonAsync("/api/assistant/ask", new { question = "Come funziona la fattura elettronica?" });
        response.EnsureSuccessStatusCode();
        var answer = (await response.Content.ReadFromJsonAsync<AiAssistantResponse>())!;
        Assert.Equal("stub", answer.Provider);
        Assert.Contains("Fattur", answer.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ask_WhenOff_ReturnsServiceUnavailable()
    {
        using var authed = _fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Ai:Provider", "off")).CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _fixture.Admin.Token);
        var response = await authed.PostAsJsonAsync("/api/assistant/ask", new { question = "test" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
