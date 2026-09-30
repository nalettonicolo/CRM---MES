using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

/// <summary>Company made of activities and departments: a machine builder machines, assembles, wires,
/// tests and services. Departments become areas with their work centers, and filter what operators see.</summary>
public class CompanyStructureTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public CompanyStructureTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    [Fact]
    public void MachineBuilder_GetsTheDepartmentsOfTheTrade()
    {
        var departments = Departments.SuggestedFor(["machine-building"]);

        Assert.Contains("machining", departments);
        Assert.Contains("assembly", departments);
        Assert.Contains("panels", departments);
        Assert.Contains("testing", departments);
        Assert.Contains("service", departments);
        Assert.Contains("sales", departments);          // every company has these
        Assert.DoesNotContain("food-production", departments);
    }

    [Fact]
    public void Modules_ComeFromActivitiesAndDepartments_OnlyWhenAvailable()
    {
        var modules = Departments.SuggestedModules(["installations"], ["panels", "service"]);

        Assert.Contains("site-work", modules);            // from the activity and the service department
        Assert.Contains("panel-verification", modules);   // from the panel builders department
        Assert.DoesNotContain("haccp", modules);
        Assert.DoesNotContain("metel", modules);          // announced, not available yet
    }

    [Fact]
    public async Task Profile_KeepsSeveralActivities_WithTheFirstAsMainSector()
    {
        var response = await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Costruzioni Aurora spa", null, null, null, null, null, null, ["machine-building", "electrical-panels"]));
        var profile = await response.Content.ReadFromJsonAsync<CompanyProfileResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("machine-building", profile!.Sector);
        Assert.Equal(["machine-building", "electrical-panels"], profile.Activities);
        Assert.Contains("panel-verification", profile.EnabledModules);

        var bad = await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "X", null, null, null, null, null, null, ["machine-building", "spaziale"]));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Structure_CreatesDepartmentsAndTheirWorkCenters_Once()
    {
        // A work center made by hand before the configuration: adopted, not duplicated.
        await _admin.PostAsJsonAsync("/api/work-centers", new CreateWorkCenterRequest("CABL", "Banco cablaggio", null, 480));

        var request = new SaveStructureRequest(
        [
            new SaveDepartmentRequest("machining", "Officina", null),
            new SaveDepartmentRequest("panels", "Quadristi", null),
            new SaveDepartmentRequest("assembly", "Montaggio linea 1", null),
            new SaveDepartmentRequest("assembly", "Montaggio linea 2", null),
            new SaveDepartmentRequest("sales", "Ufficio commerciale", null, CreateWorkCenters: false),
        ]);

        var first = await (await _admin.PutAsJsonAsync("/api/company-profile/structure", request)).Content.ReadFromJsonAsync<StructureResultResponse>();
        var second = await (await _admin.PutAsJsonAsync("/api/company-profile/structure", request)).Content.ReadFromJsonAsync<StructureResultResponse>();

        Assert.Equal(5, first!.CreatedDepartments);
        Assert.Equal(5, first.CreatedWorkCenters); // CNC, TORNIO, FRESA, MONT, MONT-<code>; CABL adopted
        Assert.Equal(0, second!.CreatedDepartments);
        Assert.Equal(0, second.CreatedWorkCenters);

        var structure = await _admin.GetFromJsonAsync<List<DepartmentResponse>>("/api/company-profile/structure");
        Assert.Equal(3, structure!.Single(d => d.Name == "Officina").WorkCenterCount);
        Assert.Equal(1, structure.Single(d => d.Name == "Quadristi").WorkCenterCount);
        Assert.Equal(1, structure.Single(d => d.Name == "Montaggio linea 2").WorkCenterCount);
        Assert.Equal("assembly", structure.Single(d => d.Name == "Montaggio linea 1").Type);
    }

    [Fact]
    public async Task Structure_RefusesDuplicatesUnknownTypes_AndNonAdmins()
    {
        var duplicate = await _admin.PutAsJsonAsync("/api/company-profile/structure", new SaveStructureRequest(
            [new SaveDepartmentRequest("testing", "Collaudo", null), new SaveDepartmentRequest("testing", "collaudo", null)]));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("due volte", await duplicate.Content.ReadAsStringAsync());

        var unknown = await _admin.PutAsJsonAsync("/api/company-profile/structure", new SaveStructureRequest(
            [new SaveDepartmentRequest("astronauti", "Spazio", null)]));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var sales = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Sales");
        var notAdmin = await _fixture.Factory.AuthenticatedClient(sales.Token).PutAsJsonAsync("/api/company-profile/structure",
            new SaveStructureRequest([new SaveDepartmentRequest("testing", "Collaudo", null)]));
        Assert.Equal(HttpStatusCode.Forbidden, notAdmin.StatusCode);
    }

    [Fact]
    public async Task Operators_SeeTheirDepartmentsJobsFirst_AndEverythingOnRequest()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        await _admin.PutAsJsonAsync("/api/company-profile/structure", new SaveStructureRequest(
        [
            new SaveDepartmentRequest("panels", $"Cablaggio {suffix}", null),
            new SaveDepartmentRequest("testing", $"Collaudo {suffix}", null),
        ]));
        var structure = await _admin.GetFromJsonAsync<List<DepartmentResponse>>("/api/company-profile/structure");
        var panels = structure!.Single(d => d.Name == $"Cablaggio {suffix}");
        var workCenters = await _admin.GetFromJsonAsync<List<WorkCenter>>("/api/work-centers");
        var wiringBench = workCenters!.First(w => w.AreaId == panels.Id).Name;
        var testRoom = workCenters!.First(w => w.AreaId == structure.Single(d => d.Name == $"Collaudo {suffix}").Id).Name;

        var wiringJob = await ReleasedJobAsync($"W{suffix}", wiringBench);
        var testJob = await ReleasedJobAsync($"T{suffix}", testRoom);

        var wirer = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        (await _admin.PostAsync($"/api/areas/{panels.Id}/users/{wirer.UserId}", null)).EnsureSuccessStatusCode();
        var wirerClient = _fixture.Factory.AuthenticatedClient(wirer.Token);

        var mine = await wirerClient.GetFromJsonAsync<List<WorkOrderLookupResponse>>("/api/work-orders/lookup?department=mine");
        Assert.Contains(mine!, o => o.Id == wiringJob);
        Assert.DoesNotContain(mine!, o => o.Id == testJob);

        var all = await wirerClient.GetFromJsonAsync<List<WorkOrderLookupResponse>>("/api/work-orders/lookup");
        Assert.Contains(all!, o => o.Id == testJob);

        // A shared terminal logged in as someone else: the operator identified by PIN decides.
        var forWirer = await _admin.GetFromJsonAsync<List<WorkOrderLookupResponse>>($"/api/work-orders/lookup?department=mine&forUser={wirer.UserId}");
        Assert.DoesNotContain(forWirer!, o => o.Id == testJob);

        // The technicians' phone page: the same filter on the jobs it offers.
        var phoneMine = await wirerClient.GetFromJsonAsync<List<SiteWorkOrderResponse>>("/api/site-reports/open-work-orders?department=mine");
        Assert.Contains(phoneMine!, o => o.Id == wiringJob);
        Assert.DoesNotContain(phoneMine!, o => o.Id == testJob);
        var phoneAll = await wirerClient.GetFromJsonAsync<List<SiteWorkOrderResponse>>("/api/site-reports/open-work-orders");
        Assert.Contains(phoneAll!, o => o.Id == testJob);

        var departments = await wirerClient.GetFromJsonAsync<List<UserDepartmentResponse>>("/api/areas/of-user");
        Assert.Equal("panels", departments!.Single().DepartmentType);

        // Someone in no department sees everything.
        var newcomer = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        var newcomerView = await _fixture.Factory.AuthenticatedClient(newcomer.Token)
            .GetFromJsonAsync<List<WorkOrderLookupResponse>>("/api/work-orders/lookup?department=mine");
        Assert.Contains(newcomerView!, o => o.Id == testJob);
    }

    private async Task<Guid> ReleasedJobAsync(string code, string workCenter)
    {
        var product = await (await _admin.PostAsJsonAsync("/api/products", new CreateProductRequest($"P-{code}", $"Prodotto {code}", null)))
            .Content.ReadFromJsonAsync<ProductSummaryResponse>();
        (await _admin.PutAsJsonAsync($"/api/products/{product!.Id}/routing",
            new ReplaceRoutingRequest([new RoutingStepRequest("Lavorazione", null, workCenter, 60)]))).EnsureSuccessStatusCode();
        var order = await (await _admin.PostAsJsonAsync("/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null)))
            .Content.ReadFromJsonAsync<WorkOrderResponse>();
        (await _admin.PostAsync($"/api/work-orders/{order!.Id}/release", null)).EnsureSuccessStatusCode();
        return order.Id;
    }
}
