using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

/// <summary>The profile is a single row per database, so the tests that write it run as one sequence
/// in a single test to stay independent of xUnit's ordering.</summary>
public class CompanyProfileTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public CompanyProfileTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    [Fact]
    public async Task Profile_StartsUnconfiguredWithEverything_ThenFollowsTheChosenSector()
    {
        var initial = await _adminClient.GetFromJsonAsync<CompanyProfileResponse>("/api/company-profile");
        Assert.False(initial!.IsConfigured);
        Assert.Equal(Sectors.Modules.Count, initial.EnabledModules.Count);

        // No module list: the sector's preset applies.
        var saved = await SaveAsync(new SaveCompanyProfileRequest(
            "  Officina Rossi srl ", "IT01234567890", "Via Po 1, Torino", null, null, "mechanical", null));
        Assert.True(saved.IsConfigured);
        Assert.Equal("Officina Rossi srl", saved.CompanyName);
        Assert.Contains("subcontracting", saved.EnabledModules);
        Assert.DoesNotContain("panel-verification", saved.EnabledModules);
        Assert.DoesNotContain("metel", saved.EnabledModules);

        // Changing sector with an explicit list keeps exactly that list (normalized, no duplicates).
        saved = await SaveAsync(new SaveCompanyProfileRequest(
            "Quadri Bianchi srl", null, null, null, null, "electrical-panels", ["sales", "Metel", "metel", "panel-verification"]));
        Assert.Equal("electrical-panels", saved.Sector);
        Assert.Equal(["sales", "metel", "panel-verification"], saved.EnabledModules);

        // Still one row, read back as saved.
        var reloaded = await _adminClient.GetFromJsonAsync<CompanyProfileResponse>("/api/company-profile");
        Assert.Equal("Quadri Bianchi srl", reloaded!.CompanyName);
        Assert.Equal(saved.EnabledModules, reloaded.EnabledModules);
    }

    [Theory]
    [InlineData("", "generic", null)]
    [InlineData("Azienda", "astronautica", null)]
    [InlineData("Azienda", "generic", "teletrasporto")]
    public async Task Save_RejectsMissingNameUnknownSectorOrModule(string name, string sector, string? module)
    {
        var response = await _adminClient.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            name, null, null, null, null, sector, module is null ? null : ["sales", module]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OtherRoles_CanReadTheProfile_ButOnlyAdminChangesIt()
    {
        var auth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Management", "profilo-dir");
        var client = _fixture.Factory.AuthenticatedClient(auth.Token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/company-profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/company-profile/catalog")).StatusCode);
        var response = await client.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Azienda", null, null, null, null, "generic", null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_EverySectorPresetUsesKnownModules()
    {
        var catalog = await _adminClient.GetFromJsonAsync<CompanyCatalogResponse>("/api/company-profile/catalog");
        Assert.Equal(Sectors.All.Count, catalog!.Sectors.Count);
        var moduleKeys = catalog.Modules.Select(module => module.Key).ToHashSet();
        Assert.All(catalog.Sectors, sector =>
        {
            Assert.NotEmpty(sector.Modules);
            Assert.All(sector.Modules, key => Assert.Contains(key, moduleKeys));
            Assert.Equal(sector.Modules.Count, sector.Modules.Distinct().Count());
        });
        Assert.Contains(catalog.Sectors, sector => sector.Key == "generic");
    }

    private async Task<CompanyProfileResponse> SaveAsync(SaveCompanyProfileRequest request)
    {
        var response = await _adminClient.PutAsJsonAsync("/api/company-profile", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompanyProfileResponse>())!;
    }
}
