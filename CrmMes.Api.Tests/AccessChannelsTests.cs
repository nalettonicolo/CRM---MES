using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

/// <summary>Channels (desktop program, web platform, technicians' phone page) configured by the Admin:
/// who may log in from where, and which areas each channel shows.</summary>
public class AccessChannelsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private const string TestPassword = "TestPass123!";
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public AccessChannelsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    [Fact]
    public void NothingConfigured_MeansEverythingEverywhere()
    {
        var settings = AccessChannels.Parse(null);

        foreach (var channel in AccessChannels.All)
        {
            Assert.True(AccessChannels.CanLogIn(settings, "Operator", channel));
        }

        Assert.Contains("invoicing", AccessChannels.AreasFor(settings, ["invoicing"], AccessChannels.Web));
    }

    [Fact]
    public void Admin_CanAlwaysLogIn_FromEveryChannelTheCompanyUses()
    {
        var settings = new AccessChannels.Settings
        {
            Channels = [AccessChannels.Web],
            Roles = new() { ["Admin"] = [AccessChannels.Desktop] },
        };

        Assert.True(AccessChannels.CanLogIn(settings, "Admin", AccessChannels.Web));
        Assert.False(AccessChannels.CanLogIn(settings, "Admin", AccessChannels.Desktop));
    }

    [Fact]
    public void Validate_RequiresDesktopOrWeb_AndKnownNames()
    {
        Assert.NotNull(AccessChannels.Validate(new AccessChannels.Settings { Channels = [AccessChannels.Mobile] }));
        Assert.NotNull(AccessChannels.Validate(new AccessChannels.Settings { Roles = new() { ["Boss"] = ["web"] } }));
        Assert.NotNull(AccessChannels.Validate(new AccessChannels.Settings { Areas = new() { ["invoicing"] = ["mobile"] } }));
        Assert.NotNull(AccessChannels.Validate(new AccessChannels.Settings { Areas = new() { ["nowhere"] = ["web"] } }));
        Assert.Null(AccessChannels.Validate(new AccessChannels.Settings { Channels = [AccessChannels.Web, AccessChannels.Mobile] }));
    }

    [Fact]
    public void Areas_FollowTheEnabledModules_AndTheChannel()
    {
        var settings = new AccessChannels.Settings { Areas = new() { ["invoicing"] = [AccessChannels.Desktop] } };

        var web = AccessChannels.AreasFor(settings, ["invoicing", "sales"], AccessChannels.Web);
        var desktop = AccessChannels.AreasFor(settings, ["invoicing", "sales"], AccessChannels.Desktop);

        Assert.DoesNotContain("invoicing", web);
        Assert.Contains("invoicing", desktop);
        Assert.Contains("sales", web);
        Assert.DoesNotContain("haccp", desktop);
        Assert.Contains("production", web);
    }

    [Fact]
    public async Task AdminSettings_DecideWhoLogsInFromWhere_AndWhatEachChannelShows()
    {
        var sales = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Sales");
        var operatorUser = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        await ConfigureCompanyAsync();

        var saved = await _admin.PutAsJsonAsync("/api/company-profile/access", new SaveAccessChannelsRequest(
            [AccessChannels.Desktop, AccessChannels.Web],
            new() { ["Sales"] = [AccessChannels.Web] },
            new() { ["invoicing"] = [AccessChannels.Desktop] }));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var salesFromDesktop = await LoginAsync(sales.Email, null);
        Assert.Equal(HttpStatusCode.Forbidden, salesFromDesktop.StatusCode);
        Assert.Contains("programma desktop", await salesFromDesktop.Content.ReadAsStringAsync());

        var salesFromWeb = await LoginAsync(sales.Email, "web");
        Assert.Equal(HttpStatusCode.OK, salesFromWeb.StatusCode);
        var auth = await salesFromWeb.Content.ReadFromJsonAsync<AuthResponse>();
        var channelClaim = new JwtSecurityTokenHandler().ReadJwtToken(auth!.Token).Claims.Single(c => c.Type == "channel");
        Assert.Equal("web", channelClaim.Value);

        var operatorFromPhone = await LoginAsync(operatorUser.Email, "mobile");
        Assert.Equal(HttpStatusCode.Forbidden, operatorFromPhone.StatusCode);
        Assert.Contains("non usa", await operatorFromPhone.Content.ReadAsStringAsync());

        var webAreas = await _admin.GetFromJsonAsync<List<string>>("/api/company-profile/areas?channel=web");
        var desktopAreas = await _admin.GetFromJsonAsync<List<string>>("/api/company-profile/areas?channel=desktop");
        Assert.DoesNotContain("invoicing", webAreas!);
        Assert.Contains("invoicing", desktopAreas!);

        // The Admin moves Sales back to the desktop: the open web session ends at its next renewal.
        await _admin.PutAsJsonAsync("/api/company-profile/access", new SaveAccessChannelsRequest(
            [AccessChannels.Desktop, AccessChannels.Web], new() { ["Sales"] = [AccessChannels.Desktop] }, null));
        var renewal = await _fixture.Client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Forbidden, renewal.StatusCode);
    }

    [Fact]
    public async Task InvalidSettings_AndUnknownChannels_AreRefused()
    {
        await ConfigureCompanyAsync();

        var noOfficeChannel = await _admin.PutAsJsonAsync("/api/company-profile/access",
            new SaveAccessChannelsRequest([AccessChannels.Mobile], null, null));
        Assert.Equal(HttpStatusCode.BadRequest, noOfficeChannel.StatusCode);

        var unknownChannel = await LoginAsync(AdminSeededApiTestFixture.AdminEmail, "fax", AdminSeededApiTestFixture.AdminPassword);
        Assert.Equal(HttpStatusCode.BadRequest, unknownChannel.StatusCode);

        var operatorUser = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        var operatorClient = _fixture.Factory.AuthenticatedClient(operatorUser.Token);
        var notAdmin = await operatorClient.PutAsJsonAsync("/api/company-profile/access",
            new SaveAccessChannelsRequest([AccessChannels.Desktop], null, null));
        Assert.Equal(HttpStatusCode.Forbidden, notAdmin.StatusCode);
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string? channel, string password = TestPassword) =>
        _fixture.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, channel));

    private async Task ConfigureCompanyAsync()
    {
        var response = await _admin.PutAsJsonAsync("/api/company-profile",
            new SaveCompanyProfileRequest("Officine Canali srl", null, null, null, null, "generic", null));
        response.EnsureSuccessStatusCode();
    }
}
