using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class SoftwareOriginTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _admin;

    public SoftwareOriginTests(AdminSeededApiTestFixture fixture) =>
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);

    [Fact]
    public async Task Get_ReturnsDefaultItalianDeclaration_AboveThreshold()
    {
        var doc = (await _admin.GetFromJsonAsync<SoftwareOriginDeclarationResponse>(
            "/api/company-profile/software-origin"))!;
        Assert.Equal("Nicolò MES", doc.ProductName);
        Assert.True(doc.EuDevelopmentPercent >= 50m);
        Assert.True(doc.MeetsEuThreshold);
        Assert.Contains("Unione europea", doc.DeclarationText);
    }

    [Fact]
    public async Task Save_BelowThreshold_FlagsWarning()
    {
        var saved = (await (await _admin.PutAsJsonAsync("/api/company-profile/software-origin",
            new SaveSoftwareOriginRequest(40m, "Svizzera", "Mario Rossi, Amministratore")))
            .Content.ReadFromJsonAsync<SoftwareOriginDeclarationResponse>())!;
        Assert.Equal(40m, saved.EuDevelopmentPercent);
        Assert.False(saved.MeetsEuThreshold);
        Assert.Equal("Svizzera", saved.DevelopmentPlaces);

        var restored = (await (await _admin.PutAsJsonAsync("/api/company-profile/software-origin",
            new SaveSoftwareOriginRequest(100m, "Italia", null)))
            .Content.ReadFromJsonAsync<SoftwareOriginDeclarationResponse>())!;
        Assert.True(restored.MeetsEuThreshold);
    }

    [Fact]
    public async Task Save_InvalidPercent_ReturnsBadRequest()
    {
        var response = await _admin.PutAsJsonAsync("/api/company-profile/software-origin",
            new SaveSoftwareOriginRequest(120m, "Italia", null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
