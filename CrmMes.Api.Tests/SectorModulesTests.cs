using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

/// <summary>The sector-specific modules: CEI EN 61439 panel verification (panel builders), lot expiry
/// with first-expiring-first-out picking (food), and the DDT export for the accounting system.</summary>
public class SectorModulesTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public SectorModulesTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<WorkOrderResponse> CreateWorkOrderAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = (await (await _adminClient.PostAsJsonAsync("/api/products", new CreateProductRequest($"QE-{suffix}", $"Quadro {suffix}", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        var response = await _adminClient.PostAsJsonAsync("/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, "Cliente Quadri", null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
    }

    private static SavePanelVerificationRequest Verification(PanelVerificationResponse from, string? result, decimal? voltage = 400, decimal? current = 250) => new(
        "CEI EN 61439-2", "Sistema XY", "Armadio XY-2000", "SN-001", voltage, current, 50, 25, null, "IP55", "Forma 2b", "TN-S", 500, 2500,
        null, from.Checks.Select(check => new PanelVerificationCheckRequest(check.Clause, check.Description, result, null)).ToList());

    [Fact]
    public async Task PanelVerification_StartsFromTheStandardChecklist_AndCompletesOnlyWhenEverythingPassed()
    {
        var order = await CreateWorkOrderAsync();
        var url = $"/api/work-orders/{order.Id}/panel-verification";

        var blank = (await _adminClient.GetFromJsonAsync<PanelVerificationResponse>(url))!;
        Assert.False(blank.IsSaved);
        Assert.Equal(PanelVerificationController.RoutineChecks.Count, blank.Checks.Count);
        Assert.Equal("11.2", blank.Checks[0].Clause);
        Assert.Equal("11.10", blank.Checks[^1].Clause); // numeric order, not text order

        // Not every check done: saved, but can't complete.
        var partial = Verification(blank, "Pass") with { Checks = [.. Verification(blank, "Pass").Checks!.Take(3), .. Verification(blank, null).Checks!.Skip(3)] };
        (await _adminClient.PutAsJsonAsync(url, partial)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await _adminClient.PostAsync($"{url}/complete", null)).StatusCode);

        // A failed check blocks completion too.
        (await _adminClient.PutAsJsonAsync(url, Verification(blank, "Fail"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await _adminClient.PostAsync($"{url}/complete", null)).StatusCode);

        // Rated data are required on the declaration.
        (await _adminClient.PutAsJsonAsync(url, Verification(blank, "Pass", voltage: null))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await _adminClient.PostAsync($"{url}/complete", null)).StatusCode);

        var saved = await _adminClient.PutAsJsonAsync(url, Verification(blank, "Pass") with
        {
            Checks = [.. Verification(blank, "Pass").Checks!.SkipLast(1), new PanelVerificationCheckRequest("11.10", "Cablaggio e funzionamento", "NotApplicable", "Nessun ausiliario")]
        });
        saved.EnsureSuccessStatusCode();
        var completeResponse = await _adminClient.PostAsync($"{url}/complete", null);
        completeResponse.EnsureSuccessStatusCode();
        var completed = (await completeResponse.Content.ReadFromJsonAsync<PanelVerificationResponse>())!;
        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(400, completed.RatedVoltage);
        Assert.Equal(order.Code, completed.WorkOrderCode);
        Assert.Equal("Cliente Quadri", completed.CustomerName);

        // Frozen: no edits; only an Admin reopens.
        Assert.Equal(HttpStatusCode.Conflict, (await _adminClient.PutAsJsonAsync(url, Verification(blank, "Pass"))).StatusCode);
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "collaudo-op");
        var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync($"{url}/reopen", null)).StatusCode);
        (await _adminClient.PostAsync($"{url}/reopen", null)).EnsureSuccessStatusCode();
        (await operatorClient.PutAsJsonAsync(url, Verification(blank, "Pass"))).EnsureSuccessStatusCode(); // testers fill it in
    }

    [Fact]
    public async Task PanelVerification_RejectsUnknownResultsAndNegativeValues()
    {
        var order = await CreateWorkOrderAsync();
        var url = $"/api/work-orders/{order.Id}/panel-verification";
        var blank = (await _adminClient.GetFromJsonAsync<PanelVerificationResponse>(url))!;

        Assert.Equal(HttpStatusCode.BadRequest, (await _adminClient.PutAsJsonAsync(url, Verification(blank, "Forse"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _adminClient.PutAsJsonAsync(url, Verification(blank, "Pass", current: -1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _adminClient.PutAsJsonAsync(url, Verification(blank, "Pass") with { Standard = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _adminClient.GetAsync($"/api/work-orders/{Guid.NewGuid()}/panel-verification")).StatusCode);
    }

    [Fact]
    public async Task Withdrawal_PicksTheLotExpiringFirst_BeforeOlderLotsWithoutExpiry()
    {
        var code = $"LAT-{Guid.NewGuid():N}"[..12];
        (await _adminClient.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(code, "Latte UHT", "lt", 0, 0))).EnsureSuccessStatusCode();
        var area = (await (await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {code}", $"A-{code}")))
            .Content.ReadFromJsonAsync<Area>())!;

        // Received in this order: no expiry, late expiry, early expiry.
        (await _adminClient.PostAsJsonAsync("/api/material-lots", new CreateMaterialLotRequest(code, "SENZA-SCAD", 10, null))).EnsureSuccessStatusCode();
        (await _adminClient.PostAsJsonAsync("/api/material-lots", new CreateMaterialLotRequest(code, "SCAD-TARDI", 10, null, DateTime.UtcNow.Date.AddDays(60)))).EnsureSuccessStatusCode();
        var early = (await (await _adminClient.PostAsJsonAsync("/api/material-lots", new CreateMaterialLotRequest(code, "SCAD-PRESTO", 4, null)))
            .Content.ReadFromJsonAsync<MaterialLotSummaryResponse>())!;
        var expiryResponse = await _adminClient.PutAsJsonAsync($"/api/material-lots/{early.Id}/expiry", new SetLotExpiryRequest(DateTime.UtcNow.Date.AddDays(5)));
        expiryResponse.EnsureSuccessStatusCode();

        var slip = (await (await _adminClient.PostAsJsonAsync("/api/withdrawal-slips", new CreateWithdrawalSlipRequest(
            area.Id, _fixture.Admin.UserId, null, null, [new CreateWithdrawalSlipItemRequest(code, 6, null, null)])))
            .Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;
        (await _adminClient.PostAsync($"/api/withdrawal-slips/{slip.Id}/close", null)).EnsureSuccessStatusCode();

        var lots = (await _adminClient.GetFromJsonAsync<List<MaterialLotSummaryResponse>>($"/api/material-lots?materialCode={code}"))!;
        Assert.Equal(0, lots.Single(l => l.LotNumber == "SCAD-PRESTO").Quantity);   // 4 taken first
        Assert.Equal(8, lots.Single(l => l.LotNumber == "SCAD-TARDI").Quantity);    // then 2
        Assert.Equal(10, lots.Single(l => l.LotNumber == "SENZA-SCAD").Quantity);   // untouched, although oldest

        var expiring = (await _adminClient.GetFromJsonAsync<List<MaterialLotSummaryResponse>>("/api/material-lots/expiring?days=90"))!;
        Assert.Contains(expiring, lot => lot.LotNumber == "SCAD-TARDI" && lot.MaterialCode == code);
        Assert.DoesNotContain(expiring, lot => lot.LotNumber == "SCAD-PRESTO" && lot.MaterialCode == code); // used up
    }

    [Fact]
    public async Task Export_ListsIssuedDocumentLinesOfThePeriod_WithoutDraftsOrCancelled()
    {
        var recipient = $"Contabile {Guid.NewGuid():N}"[..20];
        async Task<TransportDocumentResponse> Create(string description)
        {
            var response = await _adminClient.PostAsJsonAsync("/api/transport-documents", new SaveTransportDocumentRequest(
                "Sale", null, null, null, recipient, null, "IT00011122233", null, "Sender", null, null, null, null, null, null, null, null, null,
                [new SaveTransportDocumentLineRequest(null, null, "A1", description, 2, "pz", null, null),
                 new SaveTransportDocumentLineRequest(null, null, "A2", description + " bis", 1, "pz", null, null)]));
            return (await response.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
        }

        var issued = await Create("Emesso");
        (await _adminClient.PostAsJsonAsync($"/api/transport-documents/{issued.Id}/issue", new IssueTransportDocumentRequest(null))).EnsureSuccessStatusCode();
        var cancelled = await Create("Annullato");
        (await _adminClient.PostAsJsonAsync($"/api/transport-documents/{cancelled.Id}/issue", new IssueTransportDocumentRequest(null))).EnsureSuccessStatusCode();
        (await _adminClient.PostAsJsonAsync($"/api/transport-documents/{cancelled.Id}/cancel", new CancelTransportDocumentRequest("errore"))).EnsureSuccessStatusCode();
        await Create("Bozza");

        var today = DateTime.UtcNow.Date;
        var rows = (await _adminClient.GetFromJsonAsync<List<TransportDocumentExportRow>>(
            $"/api/transport-documents/export?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}"))!
            .Where(row => row.RecipientName == recipient).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.StartsWith("Emesso", row.Description));
        Assert.Equal([1, 2], rows.Select(row => row.LineNumber));
        Assert.Equal("Vendita", rows[0].Reason);
        Assert.Equal("IT00011122233", rows[0].RecipientVatNumber);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _adminClient.GetAsync($"/api/transport-documents/export?from={today:yyyy-MM-dd}&to={today.AddDays(-3):yyyy-MM-dd}")).StatusCode);
    }
}
