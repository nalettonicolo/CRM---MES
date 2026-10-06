using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

public class GtinTests : FoodAndSiteTestBase
{
    public GtinTests(AdminSeededApiTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public void Gtin_CheckDigitIsVerified_ForEveryLength()
    {
        Assert.True(Gs1.IsValidGtin("4006381333931"));  // GTIN-13
        Assert.True(Gs1.IsValidGtin("96385074"));       // GTIN-8
        Assert.True(Gs1.IsValidGtin("10614141000415")); // GTIN-14
        Assert.False(Gs1.IsValidGtin("4006381333932")); // check digit wrong
        Assert.False(Gs1.IsValidGtin("400638133393"));  // 12 digits, not a GTIN length
        Assert.False(Gs1.IsValidGtin("40063813339X1")); // not digits
        Assert.False(Gs1.IsValidGtin(null));
    }

    [Fact]
    public async Task Gtin_SetClearAndKeepUnique()
    {
        var first = await CreateProductAsync();
        var second = await CreateProductAsync();

        var set = await Admin.PutAsJsonAsync($"/api/products/{first.Id}/gtin", new SetProductGtinRequest("4006381333931"));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("4006381333931", (await set.Content.ReadFromJsonAsync<ProductResponse>())!.Gtin);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PutAsJsonAsync($"/api/products/{second.Id}/gtin", new SetProductGtinRequest("4006381333932"))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin.PutAsJsonAsync($"/api/products/{second.Id}/gtin", new SetProductGtinRequest("4006381333931"))).StatusCode);

        var cleared = await Admin.PutAsJsonAsync($"/api/products/{first.Id}/gtin", new SetProductGtinRequest("  "));
        Assert.Null((await cleared.Content.ReadFromJsonAsync<ProductResponse>())!.Gtin);

        var reused = await Admin.PutAsJsonAsync($"/api/products/{second.Id}/gtin", new SetProductGtinRequest("4006381333931"));
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
    }

    [Fact]
    public async Task Gtin_OnlyWarehouseSetsIt()
    {
        var product = await CreateProductAsync();
        var auth = await TestAuth.CreateUserWithRoleAsync(Fixture.Factory, Fixture.Admin.Token, "Operator", "gtin-op");
        var operatorClient = Fixture.Factory.AuthenticatedClient(auth.Token);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await operatorClient.PutAsJsonAsync($"/api/products/{product.Id}/gtin", new SetProductGtinRequest("96385074"))).StatusCode);
    }

    private async Task<ProductResponse> CreateProductAsync()
    {
        var suffix = Suffix();
        var response = await Admin.PostAsJsonAsync("/api/products", new CreateProductRequest($"GTIN-{suffix}", $"Prodotto {suffix}", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }
}

public class HaccpTemplateTests : FoodAndSiteTestBase
{
    public HaccpTemplateTests(AdminSeededApiTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Templates_AreListed_WithUniqueKeys()
    {
        var templates = (await Admin.GetFromJsonAsync<List<HaccpTemplateResponse>>("/api/haccp/templates"))!;

        Assert.True(templates.Count >= 6);
        Assert.Equal(templates.Count, templates.Select(t => t.Key).Distinct().Count());
        Assert.All(templates, template => Assert.NotEmpty(template.Points));
        Assert.Contains(templates, template => template.Key == "cottura");
    }

    [Fact]
    public async Task ApplyingAModel_AddsItsPoints_AndTwiceAddsNothingNew()
    {
        var first = await Admin.PostAsync("/api/haccp/templates/cottura/apply", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = (await first.Content.ReadFromJsonAsync<ApplyHaccpTemplateResponse>())!;
        Assert.Equal(1, firstResult.Created);
        Assert.Equal(0, firstResult.Skipped);

        var again = (await (await Admin.PostAsync("/api/haccp/templates/cottura/apply", null))
            .Content.ReadFromJsonAsync<ApplyHaccpTemplateResponse>())!;
        Assert.Equal(0, again.Created);
        Assert.Equal(1, again.Skipped);

        var points = (await Admin.GetFromJsonAsync<List<HaccpControlPointResponse>>("/api/haccp/control-points"))!;
        var cooking = Assert.Single(points, point => point.Name == "Temperatura al cuore a fine cottura");
        Assert.True(cooking.IsNumeric);
        Assert.Equal(72m, cooking.MinValue);
        Assert.Equal("°C", cooking.Unit);
    }

    [Fact]
    public async Task UnknownModel_IsNotFound_AndOnlyWarehouseApplies()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.PostAsync("/api/haccp/templates/inesistente/apply", null)).StatusCode);

        var auth = await TestAuth.CreateUserWithRoleAsync(Fixture.Factory, Fixture.Admin.Token, "Operator", "haccp-tpl-op");
        var operatorClient = Fixture.Factory.AuthenticatedClient(auth.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync("/api/haccp/templates/sanificazione/apply", null)).StatusCode);
    }
}
