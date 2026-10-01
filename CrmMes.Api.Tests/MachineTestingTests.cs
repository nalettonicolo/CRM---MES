using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class MachineTestingTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public MachineTestingTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<WorkOrderResponse> MachineOrderAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = (await (await _admin.PostAsJsonAsync("/api/products", new CreateProductRequest($"PRS-{suffix}", $"Pressa {suffix}", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        var response = await _admin.PostAsJsonAsync("/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
    }

    private async Task<MachineDossierResponse> Send(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MachineDossierResponse>())!;
    }

    private static SaveMachineTestRequest Filled(MachineTestResponse test, string serial, Func<MachineTestItemResponse, string> result) => new(
        serial, test.Location, DateTime.UtcNow, "Ing. Bianchi (cliente)", null,
        test.Items.Select(i => new MachineTestItemRequest(i.Section, i.Description, i.Expected, "ok", result(i), null)).ToList());

    private async Task<MachineTestResponse> PassedFatAsync(Guid workOrderId, string serial)
    {
        var dossier = await Send(await _admin.PostAsJsonAsync($"/api/machine-testing/work-orders/{workOrderId}/tests", new CreateMachineTestRequest("FAT", serial, null)));
        var test = dossier.Tests.Last();
        await Send(await _admin.PutAsJsonAsync($"/api/machine-testing/tests/{test.Id}", Filled(test, serial, _ => "Pass")));
        return (await Send(await _admin.PostAsync($"/api/machine-testing/tests/{test.Id}/close", null))).Tests.Single(t => t.Id == test.Id);
    }

    private static SaveTechnicalFileRequest CompleteFile() => new(
        MachineTestingController.TechnicalFileElements
            .Select(e => new TechnicalFileItemRequest(e.Code, null, e.Optional ? "NotApplicable" : "Present", e.Optional ? null : $"UT/{e.Code}"))
            .ToList());

    private static SaveMachineDeclarationRequest Declaration(string serial) => new(
        "Pressa idraulica", "Stampaggio a freddo di lamiera", "PI-200", "Idraulica", serial, DateTime.UtcNow.Year,
        "Direttiva 2014/30/UE", "EN ISO 12100:2010\nEN 60204-1:2018\nEN ISO 16092-3:2017", null,
        "Mario Rossi, Via Roma 1, 20100 Milano", "Milano", "Mario Rossi", "Legale rappresentante", null);

    [Fact]
    public async Task Fat_StartsFromTheStandardChecklist_AndClosesPassedOrFailed()
    {
        var order = await MachineOrderAsync();
        var url = $"/api/machine-testing/work-orders/{order.Id}/tests";

        var dossier = await Send(await _admin.PostAsJsonAsync(url, new CreateMachineTestRequest("fat", "M-001", null)));
        var test = Assert.Single(dossier.Tests);
        Assert.Equal("FAT", test.Kind);
        Assert.Equal("Draft", test.Status);
        Assert.Equal(MachineTestingController.FatChecklist.Count, test.Items.Count);
        Assert.Contains(test.Items, i => i.Description.Contains("Resistenza d'isolamento"));

        // A second FAT while one is open: refused. Closing without outcomes: refused.
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsJsonAsync(url, new CreateMachineTestRequest("FAT", "M-001", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(url, new CreateMachineTestRequest("XYZ", null, null))).StatusCode);
        var notReady = await _admin.PostAsync($"/api/machine-testing/tests/{test.Id}/close", null);
        Assert.Equal(HttpStatusCode.BadRequest, notReady.StatusCode);
        Assert.Contains("esito", await notReady.Content.ReadAsStringAsync());

        // One failed verification: the test closes as failed and is frozen.
        await Send(await _admin.PutAsJsonAsync($"/api/machine-testing/tests/{test.Id}",
            Filled(test, "M-001", i => i.Description.Contains("Arresto di emergenza") ? "Fail" : i.Description.Contains("Rumorosità") ? "NotApplicable" : "Pass")));
        var closed = await Send(await _admin.PostAsync($"/api/machine-testing/tests/{test.Id}/close", null));
        Assert.Equal("Failed", closed.Tests.Single().Status);
        Assert.Equal("Test Admin", closed.Tests.Single().TestedBy);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"/api/machine-testing/tests/{test.Id}", Filled(test, "M-001", _ => "Pass"))).StatusCode);

        // The repeated test passes; numbers go on.
        var passed = await PassedFatAsync(order.Id, "M-001");
        Assert.Equal("Passed", passed.Status);
        Assert.Equal(closed.Tests.Single().Number + 1, passed.Number);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "MachineTestClosed" && a.EntityId == order.Id && a.Details!.Contains("non superato")));
    }

    [Fact]
    public async Task Declaration_NeedsPassedTestOfThatSerial_AndCompleteFile_ThenIsNumberedAndFrozen()
    {
        (await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Officine Prova srl", "01234567890", "Via Roma 1, 20100 Milano", null, null, "machine-building", null))).EnsureSuccessStatusCode();
        var order = await MachineOrderAsync();
        var baseUrl = $"/api/machine-testing/work-orders/{order.Id}";

        var start = (await _admin.GetFromJsonAsync<MachineDossierResponse>(baseUrl))!;
        Assert.False(start.Declaration.IsSaved);
        Assert.Equal(MachineTestingController.LegalBasisOn(DateTime.UtcNow), start.Declaration.LegalBasis);
        Assert.Equal(MachineTestingController.TechnicalFileElements.Count, start.TechnicalFile.Count);
        Assert.False(start.TechnicalFileComplete);

        // Not saved yet / missing test and file.
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsync($"{baseUrl}/declaration/issue", null)).StatusCode);
        await Send(await _admin.PutAsJsonAsync($"{baseUrl}/declaration", Declaration("M-77")));
        var missing = await _admin.PostAsync($"{baseUrl}/declaration/issue", null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var message = await missing.Content.ReadAsStringAsync();
        Assert.Contains("M-77", message);
        Assert.Contains("fascicolo tecnico", message);

        // A mandatory element can't be "not applicable".
        var wrongFile = new SaveTechnicalFileRequest([new TechnicalFileItemRequest("risk-assessment", null, "NotApplicable", null)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync($"{baseUrl}/technical-file", wrongFile)).StatusCode);

        // A passed test of another serial number doesn't count.
        await PassedFatAsync(order.Id, "M-99");
        var file = await Send(await _admin.PutAsJsonAsync($"{baseUrl}/technical-file", new SaveTechnicalFileRequest(
            [.. CompleteFile().Items!, new TechnicalFileItemRequest(null, "Relazione sul rumore", "Present", "UT/rumore.pdf")])));
        Assert.True(file.TechnicalFileComplete);
        Assert.Contains(file.TechnicalFile, i => i.Description == "Relazione sul rumore" && i.Code.StartsWith("custom-"));
        Assert.Contains(file.MissingForDeclaration, p => p.Contains("M-77"));

        await PassedFatAsync(order.Id, "M-77");
        var issued = await Send(await _admin.PostAsync($"{baseUrl}/declaration/issue", null));
        Assert.Equal("Issued", issued.Declaration.Status);
        Assert.NotNull(issued.Declaration.Number);
        Assert.Equal(MachineTestingController.LegalBasisOn(DateTime.UtcNow), issued.Declaration.LegalBasis);
        Assert.Equal("Officine Prova srl", issued.Manufacturer.Name);
        Assert.Empty(issued.MissingForDeclaration);

        // Frozen: no edits to declaration or file, no test reopened.
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"{baseUrl}/declaration", Declaration("M-78"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"{baseUrl}/technical-file", CompleteFile())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync($"/api/machine-testing/tests/{issued.Tests.Last().Id}/reopen", null)).StatusCode);

        // Withdrawn by the Admin with a reason: draft again, same number kept.
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync($"{baseUrl}/declaration/withdraw", new WithdrawDeclarationRequest(" "))).StatusCode);
        var withdrawn = await Send(await _admin.PostAsJsonAsync($"{baseUrl}/declaration/withdraw", new WithdrawDeclarationRequest("Matricola errata")));
        Assert.Equal("Draft", withdrawn.Declaration.Status);
        Assert.Equal(issued.Declaration.Number, withdrawn.Declaration.Number);

        var list = (await _admin.GetFromJsonAsync<List<MachineTestingSummary>>($"/api/machine-testing/work-orders?search={order.Code}"))!;
        var row = Assert.Single(list);
        Assert.Equal(2, row.Tests);
        Assert.Equal("Passed", row.LastTestStatus);
        Assert.Equal(row.FileTotal, row.FileDone);
    }

    [Fact]
    public void LegalBasis_SwitchesToTheRegulationOn20January2027()
    {
        Assert.Equal("2006/42/CE", MachineTestingController.LegalBasisOn(new DateTime(2027, 1, 19, 23, 0, 0)));
        Assert.Equal("2023/1230", MachineTestingController.LegalBasisOn(new DateTime(2027, 1, 20)));
        Assert.Contains("2023/1230", MachineTestingController.LegalBasisText("2023/1230"));
    }

    [Fact]
    public async Task ShopFloor_FillsTests_ButNotTheFileOrTheDeclaration_AndCannotReopen()
    {
        var order = await MachineOrderAsync();
        var operator_ = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var client = _fixture.Factory.AuthenticatedClient(operator_.Token);
        var baseUrl = $"/api/machine-testing/work-orders/{order.Id}";

        var dossier = await Send(await client.PostAsJsonAsync($"{baseUrl}/tests", new CreateMachineTestRequest("SAT", "M-5", "Cliente, Brescia")));
        var test = dossier.Tests.Single();
        Assert.Equal(MachineTestingController.SatChecklist.Count, test.Items.Count);
        await Send(await client.PutAsJsonAsync($"/api/machine-testing/tests/{test.Id}", Filled(test, "M-5", _ => "Pass")));
        Assert.Equal("Passed", (await Send(await client.PostAsync($"/api/machine-testing/tests/{test.Id}/close", null))).Tests.Single().Status);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/machine-testing/tests/{test.Id}/reopen", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"{baseUrl}/technical-file", CompleteFile())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"{baseUrl}/declaration", Declaration("M-5"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{baseUrl}/declaration/issue", null)).StatusCode);

        var reopened = await Send(await _admin.PostAsync($"/api/machine-testing/tests/{test.Id}/reopen", null));
        Assert.Equal("Draft", reopened.Tests.Single().Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/machine-testing/work-orders/{Guid.NewGuid()}")).StatusCode);
    }
}
