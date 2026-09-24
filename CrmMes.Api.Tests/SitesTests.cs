using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

public class SitesTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public SitesTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<SiteResponse> CreateSiteAsync()
    {
        var code = $"SITE-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/sites", new CreateSiteRequest($"Sede {code}", code, "Via Test 1"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SiteResponse>())!;
    }

    [Fact]
    public async Task CreateSite_ThenList_ReturnsIt()
    {
        var site = await CreateSiteAsync();

        var listResponse = await _adminClient.GetFromJsonAsync<List<SiteResponse>>("/api/sites");

        Assert.Contains(listResponse!, s => s.Id == site.Id && s.Code == site.Code);
    }

    [Fact]
    public async Task CreateSite_DuplicateCode_ReturnsConflict()
    {
        var code = $"DUP-{Guid.NewGuid():N}"[..12];
        await _adminClient.PostAsJsonAsync("/api/sites", new CreateSiteRequest("Primo", code, null));

        var response = await _adminClient.PostAsJsonAsync("/api/sites", new CreateSiteRequest("Secondo", code, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AssignAreaToSite_ThenFilterAreasBySite_ReturnsOnlyThatArea()
    {
        var site = await CreateSiteAsync();
        var code = $"AREA-{Guid.NewGuid():N}"[..12];
        var createAreaResponse = await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {code}", code, site.Id));
        createAreaResponse.EnsureSuccessStatusCode();
        var area = (await createAreaResponse.Content.ReadFromJsonAsync<Area>())!;

        var filteredResponse = await _adminClient.GetFromJsonAsync<List<Area>>($"/api/areas?siteId={site.Id}");
        Assert.Contains(filteredResponse!, a => a.Id == area.Id);

        var detailResponse = await _adminClient.GetAsync($"/api/sites/{site.Id}/detail");
        var detail = await detailResponse.Content.ReadFromJsonAsync<SiteDetailResponse>();
        Assert.Contains(detail!.Areas, a => a.Id == area.Id);
    }

    [Fact]
    public async Task SetAreaSite_MovesAreaToAnotherSite()
    {
        var siteA = await CreateSiteAsync();
        var siteB = await CreateSiteAsync();
        var code = $"AREA-{Guid.NewGuid():N}"[..12];
        var createAreaResponse = await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {code}", code, siteA.Id));
        var area = (await createAreaResponse.Content.ReadFromJsonAsync<Area>())!;

        var moveResponse = await _adminClient.PutAsJsonAsync($"/api/areas/{area.Id}/site", new SetSiteRequest(siteB.Id));
        Assert.Equal(HttpStatusCode.NoContent, moveResponse.StatusCode);

        var detailA = await (await _adminClient.GetAsync($"/api/sites/{siteA.Id}/detail")).Content.ReadFromJsonAsync<SiteDetailResponse>();
        var detailB = await (await _adminClient.GetAsync($"/api/sites/{siteB.Id}/detail")).Content.ReadFromJsonAsync<SiteDetailResponse>();
        Assert.DoesNotContain(detailA!.Areas, a => a.Id == area.Id);
        Assert.Contains(detailB!.Areas, a => a.Id == area.Id);
    }

    [Fact]
    public async Task CreateWorkCenter_WithSiteId_AppearsInSiteDetail()
    {
        var site = await CreateSiteAsync();
        var code = $"WC-{Guid.NewGuid():N}"[..12];

        var response = await _adminClient.PostAsJsonAsync(
            "/api/work-centers", new CreateWorkCenterRequest(code, $"Centro {code}", null, 480, site.Id));
        response.EnsureSuccessStatusCode();
        var workCenter = (await response.Content.ReadFromJsonAsync<WorkCenterResponse>())!;

        var detailResponse = await _adminClient.GetAsync($"/api/sites/{site.Id}/detail");
        var detail = await detailResponse.Content.ReadFromJsonAsync<SiteDetailResponse>();

        Assert.Contains(detail!.WorkCenters, w => w.Id == workCenter.Id);
    }

    [Fact]
    public async Task DeactivateSite_RemovesItFromActiveList()
    {
        var site = await CreateSiteAsync();

        var deleteResponse = await _adminClient.DeleteAsync($"/api/sites/{site.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await _adminClient.GetFromJsonAsync<List<SiteResponse>>("/api/sites");
        Assert.DoesNotContain(listResponse!, s => s.Id == site.Id);
    }
}
