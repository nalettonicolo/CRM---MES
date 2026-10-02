using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

public class UiThemeTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _admin;

    public UiThemeTests(AdminSeededApiTestFixture fixture) =>
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);

    [Fact]
    public void Parse_Empty_ReturnsOfficinaDefaults()
    {
        var s = UiTheme.Parse(null);
        Assert.Equal(UiTheme.PresetOfficina, s.Preset);
        Assert.Equal("#CF2A1F", s.Accent);
        Assert.Equal(0, s.Radius);
    }

    [Fact]
    public void FromPreset_Carbone_IsDark()
    {
        var s = UiTheme.FromPreset(UiTheme.PresetCarbone);
        Assert.Equal("#141414", s.Background);
        Assert.Equal("#E85A4F", s.Accent);
        Assert.Contains("--sidebar-bg", UiTheme.ToCssVariables(s).Keys);
    }

    [Fact]
    public async Task Get_ReturnsDefaultTheme_WithPresets()
    {
        var theme = (await _admin.GetFromJsonAsync<UiThemeResponse>("/api/company-profile/theme"))!;
        Assert.Equal(UiTheme.PresetOfficina, theme.Preset);
        Assert.True(theme.Presets.Count >= 5);
        Assert.Equal("#CF2A1F", theme.Accent);
        Assert.True(theme.CssVariables.ContainsKey("--accent"));
    }

    [Fact]
    public async Task Save_ApplyPreset_ThenCustomAccent()
    {
        var carbone = (await (await _admin.PutAsJsonAsync("/api/company-profile/theme",
            new SaveUiThemeRequest(ApplyPreset: UiTheme.PresetCarbone)))
            .Content.ReadFromJsonAsync<UiThemeResponse>())!;
        Assert.Equal(UiTheme.PresetCarbone, carbone.Preset);
        Assert.Equal("#141414", carbone.Background);

        var custom = (await (await _admin.PutAsJsonAsync("/api/company-profile/theme",
            new SaveUiThemeRequest(
                Preset: UiTheme.PresetCarbone,
                Background: carbone.Background,
                Surface: carbone.Surface,
                SurfaceRaised: carbone.SurfaceRaised,
                Ink: carbone.Ink,
                Muted: carbone.Muted,
                Line: carbone.Line,
                Accent: "#00AA55",
                AccentHover: "#008844",
                AccentSoft: "#123322",
                OnAccent: "#041008",
                Sidebar: carbone.Sidebar,
                SidebarText: carbone.SidebarText,
                Ok: carbone.Ok,
                Warn: carbone.Warn,
                Radius: 6,
                Density: "compact",
                BackgroundStyle: "grid",
                FieldBorder: 2,
                FieldHeight: 34)))
            .Content.ReadFromJsonAsync<UiThemeResponse>())!;
        Assert.Equal("#00AA55", custom.Accent);
        Assert.Equal(6, custom.Radius);
        Assert.Equal("compact", custom.Density);
        Assert.Equal("6px", custom.CssVariables["--radius"]);

        // Restore default so other tests keep Officina look if they share the DB.
        await _admin.PutAsJsonAsync("/api/company-profile/theme",
            new SaveUiThemeRequest(ApplyPreset: UiTheme.PresetOfficina));
    }

    [Fact]
    public async Task Save_InvalidHex_IsNormalizedToFallback()
    {
        var saved = (await (await _admin.PutAsJsonAsync("/api/company-profile/theme",
            new SaveUiThemeRequest(
                Preset: UiTheme.PresetOfficina,
                Background: "not-a-color",
                Accent: "#CF2A1F",
                Surface: "#F5F5F1",
                SurfaceRaised: "#ECECE7",
                Ink: "#141414",
                Muted: "#555550",
                Line: "#C2C2BA",
                AccentHover: "#B0241A",
                AccentSoft: "#F5E4E2",
                OnAccent: "#FFF8F7",
                Sidebar: "#161616",
                SidebarText: "#A3A39C",
                Ok: "#2B5A36",
                Warn: "#8F5A10")))
            .Content.ReadFromJsonAsync<UiThemeResponse>())!;
        Assert.Equal("#E6E6E1", saved.Background);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync("/api/company-profile/theme",
            new SaveUiThemeRequest(ApplyPreset: UiTheme.PresetOfficina))).StatusCode);
    }
}
