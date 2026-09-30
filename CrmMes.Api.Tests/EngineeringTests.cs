using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class EngineeringTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public EngineeringTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private sealed record Setup(ProductResponse Product, string CodeA, string CodeB);

    /// <summary>A product at revision A: BOM 2 x A, routing Montaggio (60) + Collaudo (30).</summary>
    private async Task<Setup> ProductAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var codeA = $"EA-{suffix}";
        var codeB = $"EB-{suffix}";
        foreach (var code in new[] { codeA, codeB })
        {
            (await _admin.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(code, $"Materiale {code}", "pz", 100, 0))).EnsureSuccessStatusCode();
        }

        var product = (await (await _admin.PostAsJsonAsync("/api/products", new CreateProductRequest($"MAC-{suffix}", $"Macchina {suffix}", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        (await _admin.PutAsJsonAsync($"/api/products/{product.Id}/bom", new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(codeA, 2, null)]))).EnsureSuccessStatusCode();
        (await _admin.PutAsJsonAsync($"/api/products/{product.Id}/routing", new ReplaceRoutingRequest(
            [new RoutingStepRequest("Montaggio", null, null, 60), new RoutingStepRequest("Collaudo", null, null, 30)]))).EnsureSuccessStatusCode();
        return new Setup(product, codeA, codeB);
    }

    private async Task<WorkOrderResponse> OrderAsync(Guid productId, decimal quantity = 2)
    {
        var response = await _admin.PostAsJsonAsync("/api/work-orders", new CreateWorkOrderRequest(productId, quantity, null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
    }

    private static MultipartFormDataContent Upload(string title, byte[] data, int? step = null, string kind = "drawing", string fileName = "disegno.pdf")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(title), "title");
        form.Add(new StringContent(kind), "kind");
        if (step is not null)
        {
            form.Add(new StringContent(step.Value.ToString()), "stepSequence");
        }

        return form;
    }

    [Fact]
    public async Task NewDocumentVersion_SupersedesThePrevious_AndTheTerminalSeesOnlyItsPhase()
    {
        var setup = await ProductAsync();
        var url = $"/api/engineering/products/{setup.Product.Id}/documents";

        var v1 = (await (await _admin.PostAsync(url, Upload("Schema potenza", [1, 2, 3], step: 1, kind: "schema"))).Content.ReadFromJsonAsync<TechnicalDocumentResponse>())!;
        var v2Response = await _admin.PostAsync(url, Upload("Schema potenza", [4, 5, 6, 7], step: 1, kind: "schema"));
        Assert.Equal(HttpStatusCode.OK, v2Response.StatusCode);
        var v2 = (await v2Response.Content.ReadFromJsonAsync<TechnicalDocumentResponse>())!;
        (await _admin.PostAsync(url, Upload("Istruzioni collaudo", [9], step: 2, kind: "instructions"))).EnsureSuccessStatusCode();
        (await _admin.PostAsync(url, Upload("Disegno d'assieme", [8]))).EnsureSuccessStatusCode();

        Assert.Equal(1, v1.Version);
        Assert.Equal(2, v2.Version);
        Assert.Equal("application/pdf", v2.ContentType);
        var current = (await _admin.GetFromJsonAsync<List<TechnicalDocumentResponse>>(url))!;
        Assert.DoesNotContain(current, d => d.Id == v1.Id);
        Assert.Equal(3, current.Count);
        var history = (await _admin.GetFromJsonAsync<List<TechnicalDocumentResponse>>(url + "?history=true"))!;
        Assert.Contains(history, d => d.Id == v1.Id && !d.IsCurrent);

        var download = await _admin.GetAsync($"/api/engineering/documents/{v2.Id}/content");
        Assert.Equal([4, 5, 6, 7], await download.Content.ReadAsByteArrayAsync());

        // Terminal, phase 1 (Montaggio): its schema + the whole-product drawing, not the testing instructions.
        var order = await OrderAsync(setup.Product.Id);
        var assembly = order.Operations.Single(o => o.SequenceNumber == 1);
        var operator_ = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var terminal = _fixture.Factory.AuthenticatedClient(operator_.Token);
        var atTerminal = (await terminal.GetFromJsonAsync<List<TechnicalDocumentResponse>>($"/api/engineering/work-orders/{order.Id}/operations/{assembly.Id}/documents"))!;
        Assert.Equal(["Schema potenza", "Disegno d'assieme"], atTerminal.Select(d => d.Title));
        Assert.Equal(2, atTerminal[0].Version);
    }

    [Fact]
    public async Task Upload_RejectsUnknownPhaseAndKind_AndOperatorsCannotUpload()
    {
        var setup = await ProductAsync();
        var url = $"/api/engineering/products/{setup.Product.Id}/documents";

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsync(url, Upload("X", [1], step: 9))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsync(url, Upload("X", [1], kind: "virus"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsync(url, Upload("X", [1], fileName: "programma.exe"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsync(url, Upload("X", [1], fileName: "pagina.html"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync(url, Upload("Programma fresa", [1], kind: "cnc", fileName: "O1234.nc"))).StatusCode);

        var operator_ = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var client = _fixture.Factory.AuthenticatedClient(operator_.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(url, Upload("X", [1]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/engineering/changes",
            new EngineeringChangeRequest(setup.Product.Id, "Prova", null, null, null, [new RoutingStepRequest("Unica", null, null, 10)]))).StatusCode);
    }

    [Fact]
    public async Task AppliedChange_MovesToRevisionB_RebuildsDraftOrders_AndLeavesReleasedOnesAlone()
    {
        var setup = await ProductAsync();
        var draft = await OrderAsync(setup.Product.Id, quantity: 2);
        var released = await OrderAsync(setup.Product.Id, quantity: 1);
        (await _admin.PostAsync($"/api/work-orders/{released.Id}/release?force=true", null)).EnsureSuccessStatusCode();
        Assert.Equal("A", draft.ProductRevision);

        var create = await _admin.PostAsJsonAsync("/api/engineering/changes", new EngineeringChangeRequest(setup.Product.Id, "Nuovo cablaggio", "Aggiunta morsettiera", "Richiesta cliente",
            [new BillOfMaterialItemRequest(setup.CodeA, 2, null), new BillOfMaterialItemRequest(setup.CodeB, 4, "morsetti")],
            [new RoutingStepRequest("Carpenteria", null, null, 20), new RoutingStepRequest("Montaggio", null, null, 75), new RoutingStepRequest("Collaudo", null, null, 30)]));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var change = (await create.Content.ReadFromJsonAsync<EngineeringChangeResponse>())!;
        Assert.Equal("draft", change.Status);
        Assert.Equal(2, change.AffectedWorkOrders.Count);

        // Not approved yet: cannot be applied.
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync($"/api/engineering/changes/{change.Id}/apply", null)).StatusCode);
        (await _admin.PostAsync($"/api/engineering/changes/{change.Id}/approve", null)).EnsureSuccessStatusCode();
        var applied = (await (await _admin.PostAsync($"/api/engineering/changes/{change.Id}/apply", null)).Content.ReadFromJsonAsync<EngineeringChangeResponse>())!;

        Assert.Equal("applied", applied.Status);
        Assert.Equal("A", applied.FromRevision);
        Assert.Equal("B", applied.ToRevision);
        Assert.Contains(draft.Code, applied.ApplyReport);
        Assert.Contains(released.Code, applied.ApplyReport);

        var product = (await _admin.GetFromJsonAsync<ProductResponse>($"/api/products/{setup.Product.Id}"))!;
        Assert.Equal(2, product.BillOfMaterial.Count);
        Assert.Equal(["Carpenteria", "Montaggio", "Collaudo"], product.RoutingSteps.Select(s => s.Name));

        var draftAfter = (await _admin.GetFromJsonAsync<WorkOrderResponse>($"/api/work-orders/{draft.Id}"))!;
        Assert.Equal("B", draftAfter.ProductRevision);
        Assert.Equal(["Carpenteria", "Montaggio", "Collaudo"], draftAfter.Operations.OrderBy(o => o.SequenceNumber).Select(o => o.Name));

        var releasedAfter = (await _admin.GetFromJsonAsync<WorkOrderResponse>($"/api/work-orders/{released.Id}"))!;
        Assert.Equal("A", releasedAfter.ProductRevision);
        Assert.Equal(["Montaggio", "Collaudo"], releasedAfter.Operations.OrderBy(o => o.SequenceNumber).Select(o => o.Name));

        // Each of the 2 units of the draft has a fresh phase grid on the new operations.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var operationIds = await db.WorkOrderOperations.Where(o => o.WorkOrderId == draft.Id).Select(o => o.Id).ToListAsync();
            var grid = await db.WorkOrderUnitOperations.Where(u => operationIds.Contains(u.WorkOrderOperationId)).CountAsync();
            Assert.Equal(2 * 3, grid);
            Assert.Equal(0, await db.WorkOrderUnitOperations.CountAsync(u => draft.Operations.Select(o => o.Id).Contains(u.WorkOrderOperationId)));
        }

        // Revision A kept with its old BOM and routing; a new job starts at B.
        var revisions = (await _admin.GetFromJsonAsync<ProductRevisionsResponse>($"/api/engineering/products/{setup.Product.Id}/revisions"))!;
        Assert.Equal("B", revisions.CurrentRevision);
        var revA = Assert.Single(revisions.Archived);
        Assert.Equal("A", revA.Revision);
        Assert.Single(revA.Bom);
        Assert.Equal(["Montaggio", "Collaudo"], revA.Routing.Select(s => s.Name));
        Assert.Equal("B", (await OrderAsync(setup.Product.Id)).ProductRevision);

        // Applied once only.
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync($"/api/engineering/changes/{change.Id}/apply", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync($"/api/engineering/changes/{change.Id}/reject", null)).StatusCode);
    }

    [Fact]
    public async Task Change_MustChangeSomething_WithKnownMaterials_AndIsEditableOnlyInDraft()
    {
        var setup = await ProductAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/engineering/changes",
            new EngineeringChangeRequest(setup.Product.Id, "Niente", null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/engineering/changes",
            new EngineeringChangeRequest(setup.Product.Id, "Codice ignoto", null, null, [new BillOfMaterialItemRequest("NON-ESISTE-XYZ", 1, null)], null))).StatusCode);

        var change = (await (await _admin.PostAsJsonAsync("/api/engineering/changes",
            new EngineeringChangeRequest(setup.Product.Id, "Solo distinta", null, null, [new BillOfMaterialItemRequest(setup.CodeB, 1, null)], null)))
            .Content.ReadFromJsonAsync<EngineeringChangeResponse>())!;
        var edited = await _admin.PutAsJsonAsync($"/api/engineering/changes/{change.Id}",
            new EngineeringChangeRequest(Guid.Empty, "Solo distinta (rivista)", null, null, [new BillOfMaterialItemRequest(setup.CodeB, 3, null)], null));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(3, (await edited.Content.ReadFromJsonAsync<EngineeringChangeResponse>())!.ProposedBom![0].Quantity);

        (await _admin.PostAsync($"/api/engineering/changes/{change.Id}/reject", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.PutAsJsonAsync($"/api/engineering/changes/{change.Id}",
            new EngineeringChangeRequest(Guid.Empty, "Tardi", null, null, [new BillOfMaterialItemRequest(setup.CodeB, 1, null)], null))).StatusCode);

        // BOM-only change applied: routing untouched, revision moves on.
        var bomOnly = (await (await _admin.PostAsJsonAsync("/api/engineering/changes",
            new EngineeringChangeRequest(setup.Product.Id, "Distinta B", null, null, [new BillOfMaterialItemRequest(setup.CodeB, 1, null)], null)))
            .Content.ReadFromJsonAsync<EngineeringChangeResponse>())!;
        Assert.True(bomOnly.Number > change.Number);
        (await _admin.PostAsync($"/api/engineering/changes/{bomOnly.Id}/approve", null)).EnsureSuccessStatusCode();
        (await _admin.PostAsync($"/api/engineering/changes/{bomOnly.Id}/apply", null)).EnsureSuccessStatusCode();
        var product = (await _admin.GetFromJsonAsync<ProductResponse>($"/api/products/{setup.Product.Id}"))!;
        Assert.Equal(setup.CodeB, Assert.Single(product.BillOfMaterial).MaterialCode);
        Assert.Equal(2, product.RoutingSteps.Count);
    }

    [Theory]
    [InlineData("A", "B")]
    [InlineData("Z", "AA")]
    [InlineData("AZ", "BA")]
    [InlineData("ZZ", "AAA")]
    [InlineData("01", "02")]
    [InlineData("R09", "R10")]
    [InlineData("", "B")]
    public void NextRevision_FollowsTheUsualLetters(string current, string expected) =>
        Assert.Equal(expected, EngineeringController.NextRevision(current));
}
