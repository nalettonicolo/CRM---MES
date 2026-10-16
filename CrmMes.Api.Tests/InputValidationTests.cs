using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

/// <summary>Validazione input sistematica (iniziata 14/10/2026 nel giro "livello enterprise",
/// seconda ondata 16/10/2026 su Fornitori e Commesse): vincoli di lunghezza e formato allineati
/// alle colonne del database (vedi ApplicationDbContext) sui controller più trafficati. Non
/// sostituisce i controlli manuali esistenti (restano, vedi i singoli controller) — aggiunge solo
/// un 400 pulito prima che un input fuori misura arrivi al database, dove oggi fallirebbe con un
/// errore Postgres poco chiaro (500). I restanti controller (distinte, ordini, spedizioni, qualità,
/// service, ufficio tecnico e il resto) sono backlog esplicito in STATO-PROGETTO.md.</summary>
public class InputValidationTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _adminClient;

    public InputValidationTests(AdminSeededApiTestFixture fixture)
    {
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static string LongString(int length) => new('x', length);

    [Fact]
    public async Task CreateMaterial_NomeTroppoLungo_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(Guid.NewGuid().ToString("N")[..10], LongString(251), "pz", 10, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_GiacenzaNegativa_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(Guid.NewGuid().ToString("N")[..10], "Materiale di prova", "pz", -1, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_ScortaMinimaNegativa_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(Guid.NewGuid().ToString("N")[..10], "Materiale di prova", "pz", 10, -5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_ValoriValidi_PassaAncoraLaValidazione()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(Guid.NewGuid().ToString("N")[..10], "Materiale di prova", "pz", 10, 0));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateProduct_DescrizioneTroppoLunga_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/products",
            new CreateProductRequest(Guid.NewGuid().ToString("N")[..10], "Prodotto di prova", LongString(1001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveCustomer_EmailNonValida_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest("Cliente di prova", Guid.NewGuid().ToString("N")[..10], null, "non-e-una-email", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaveCustomer_PartitaIvaTroppoLunga_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest("Cliente di prova", Guid.NewGuid().ToString("N")[..10], LongString(41), null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_EmailNonValida_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Operatore di prova", "non-e-una-email", "Password123!", "Operator"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSupplier_EmailNonValida_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest("Fornitore di prova", Guid.NewGuid().ToString("N")[..10], "non-e-una-email", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSupplier_NomeTroppoLungo_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest(LongString(201), Guid.NewGuid().ToString("N")[..10], null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWorkOrder_CodiceTroppoLungo_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/work-orders",
            new CreateWorkOrderRequest(Guid.NewGuid(), 1, LongString(81), null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWorkOrder_NoteTroppoLunghe_RestituisceBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/work-orders",
            new CreateWorkOrderRequest(Guid.NewGuid(), 1, null, null, null, null, LongString(1001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
