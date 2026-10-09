using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Layout;

namespace CrmMes.Api.Tests;

/// <summary>Campi personalizzati: lo strumento Layout permette di aggiungere alla schermata un campo nuovo di
/// sana pianta (non solo rietichettare quelli già nel modello), e di salvarne il valore per ogni record
/// (fornitore, cliente...). Vedi CustomFieldService.</summary>
public class CustomFieldsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;
    private const string SupplierUrl = "/api/layout/" + FormLayoutRegistry.SupplierNew + "/custom-fields";
    private const string CustomerUrl = "/api/layout/" + FormLayoutRegistry.CustomerNew + "/custom-fields";
    private const string MaterialUrl = "/api/layout/" + FormLayoutRegistry.MaterialNew + "/custom-fields";

    public CustomFieldsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<CustomFieldDefinitionResponse> CreateFieldAsync(
        string url, string label, string fieldType = CustomFieldType.Text, bool required = false, int order = 1)
    {
        var response = await _admin.PostAsJsonAsync(url, new SaveCustomFieldRequest(label, fieldType, order, required));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomFieldDefinitionResponse>())!;
    }

    [Fact]
    public async Task CreateListEditAndDelete_RoundTrip()
    {
        var field = await CreateFieldAsync(SupplierUrl, $"Giorni di pagamento {Guid.NewGuid():N}", CustomFieldType.Number, required: true);
        Assert.Equal("Number", field.FieldType);
        Assert.True(field.Required);
        Assert.False(string.IsNullOrWhiteSpace(field.Key));

        var list = await _admin.GetFromJsonAsync<List<CustomFieldDefinitionResponse>>(SupplierUrl);
        Assert.Contains(list!, f => f.Id == field.Id);

        var edited = await _admin.PutAsJsonAsync($"{SupplierUrl}/{field.Id}",
            new SaveCustomFieldRequest("Giorni di pagamento (rinominato)", CustomFieldType.Number, 5, false));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editedField = await edited.Content.ReadFromJsonAsync<CustomFieldDefinitionResponse>();
        Assert.Equal("Giorni di pagamento (rinominato)", editedField!.Label);
        Assert.Equal(5, editedField.Order);
        Assert.False(editedField.Required);
        // Chiave e tipo non cambiano dopo la creazione: individuano i valori già salvati.
        Assert.Equal(field.Key, editedField.Key);

        var deleted = await _admin.DeleteAsync($"{SupplierUrl}/{field.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var afterDelete = await _admin.GetFromJsonAsync<List<CustomFieldDefinitionResponse>>(SupplierUrl);
        Assert.DoesNotContain(afterDelete!, f => f.Id == field.Id);
    }

    [Fact]
    public async Task MissingLabel_UnknownType_AndDuplicateKey_AreRefused()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(SupplierUrl,
            new SaveCustomFieldRequest(null, CustomFieldType.Text, 1, false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(SupplierUrl,
            new SaveCustomFieldRequest("Campo strano", "Colore", 1, false))).StatusCode);

        var label = $"Duplicato {Guid.NewGuid():N}";
        await CreateFieldAsync(SupplierUrl, label);
        var duplicate = await _admin.PostAsJsonAsync(SupplierUrl, new SaveCustomFieldRequest(label, CustomFieldType.Text, 2, false));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task OnlyAdmin_CanManageCustomFields_EveryoneCanRead()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var client = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(SupplierUrl,
            new SaveCustomFieldRequest("Non autorizzato", CustomFieldType.Text, 1, false))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(SupplierUrl)).StatusCode);
    }

    [Fact]
    public async Task Supplier_RequiredCustomField_IsEnforcedOnCreateAndValuesRoundTrip()
    {
        var field = await CreateFieldAsync(SupplierUrl, $"Tipologia di pagamento {Guid.NewGuid():N}", required: true);
        try
        {
            var code = $"SUP-{Guid.NewGuid():N}"[..12];

            // Senza il campo obbligatorio: rifiutato.
            var missing = await _admin.PostAsJsonAsync("/api/suppliers",
                new CreateSupplierRequest($"Fornitore {code}", code, null, null, null, null));
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

            // Con il campo: creato, e il valore si legge dal dettaglio.
            var created = await _admin.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest(
                $"Fornitore {code}", code, null, null, null,
                new Dictionary<string, string?> { [field.Key] = "Bonifico 30gg" }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var supplier = (await created.Content.ReadFromJsonAsync<SupplierResponse>())!;

            var detail = await _admin.GetFromJsonAsync<SupplierDetailResponse>($"/api/suppliers/{supplier.Id}/detail");
            Assert.Equal("Bonifico 30gg", detail!.CustomFields[field.Key]);

            // Modifica del valore.
            var edited = await _admin.PutAsJsonAsync($"/api/suppliers/{supplier.Id}", new EditSupplierRequest(
                supplier.Name, null, null, null, new Dictionary<string, string?> { [field.Key] = "RiBa 60gg" }));
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            var detailAfterEdit = await _admin.GetFromJsonAsync<SupplierDetailResponse>($"/api/suppliers/{supplier.Id}/detail");
            Assert.Equal("RiBa 60gg", detailAfterEdit!.CustomFields[field.Key]);
        }
        finally
        {
            await _admin.DeleteAsync($"{SupplierUrl}/{field.Id}");
        }
    }

    [Fact]
    public async Task Supplier_NumberCustomField_RefusesNonNumericValue()
    {
        var field = await CreateFieldAsync(SupplierUrl, $"Giorni {Guid.NewGuid():N}", CustomFieldType.Number);
        try
        {
            var code = $"SUP-{Guid.NewGuid():N}"[..12];
            var response = await _admin.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest(
                $"Fornitore {code}", code, null, null, null,
                new Dictionary<string, string?> { [field.Key] = "non-un-numero" }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await _admin.DeleteAsync($"{SupplierUrl}/{field.Id}");
        }
    }

    [Fact]
    public async Task DeletingTheDefinition_RemovesItsValuesFromTheDetail()
    {
        var field = await CreateFieldAsync(SupplierUrl, $"Da eliminare {Guid.NewGuid():N}");
        var code = $"SUP-{Guid.NewGuid():N}"[..12];
        var created = await _admin.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest(
            $"Fornitore {code}", code, null, null, null, new Dictionary<string, string?> { [field.Key] = "valore" }));
        var supplier = (await created.Content.ReadFromJsonAsync<SupplierResponse>())!;

        await _admin.DeleteAsync($"{SupplierUrl}/{field.Id}");

        var detail = await _admin.GetFromJsonAsync<SupplierDetailResponse>($"/api/suppliers/{supplier.Id}/detail");
        Assert.Empty(detail!.CustomFields);
    }

    [Fact]
    public async Task Customer_CustomField_RoundTripsThroughCreateAndDetail()
    {
        var field = await CreateFieldAsync(CustomerUrl, $"Giorni di pagamento {Guid.NewGuid():N}", CustomFieldType.Number, required: true);
        try
        {
            var code = $"CLI-{Guid.NewGuid():N}"[..12];

            var missing = await _admin.PostAsJsonAsync("/api/customers",
                new SaveCustomerRequest($"Cliente {code}", code, null, null, null, null, null));
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

            var created = await _admin.PostAsJsonAsync("/api/customers", new SaveCustomerRequest(
                $"Cliente {code}", code, null, null, null, null, null,
                new Dictionary<string, string?> { [field.Key] = "30" }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var customer = (await created.Content.ReadFromJsonAsync<CustomerResponse>())!;

            var detail = await _admin.GetFromJsonAsync<CustomerDetailResponse>($"/api/customers/{customer.Id}/detail");
            Assert.Equal("30", detail!.CustomFields[field.Key]);
        }
        finally
        {
            await _admin.DeleteAsync($"{CustomerUrl}/{field.Id}");
        }
    }

    [Fact]
    public async Task Material_RequiredCustomField_IsEnforcedOnCreateAndValuesRoundTrip()
    {
        var field = await CreateFieldAsync(MaterialUrl, $"Giorni di pagamento {Guid.NewGuid():N}", CustomFieldType.Number, required: true);
        try
        {
            var code = $"MAT-{Guid.NewGuid():N}"[..12];

            // Senza il campo obbligatorio: rifiutato.
            var missing = await _admin.PostAsJsonAsync("/api/materials",
                new CreateMaterialRequest(code, $"Materiale {code}", "pz", 10, 0));
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

            // Con il campo: creato, e il valore si legge dal dettaglio.
            var created = await _admin.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(
                code, $"Materiale {code}", "pz", 10, 0,
                new Dictionary<string, string?> { [field.Key] = "60" }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var material = (await created.Content.ReadFromJsonAsync<MaterialResponse>())!;

            var detail = await _admin.GetFromJsonAsync<MaterialResponse>($"/api/materials/{material.Id}");
            Assert.Equal("60", detail!.CustomFields![field.Key]);
        }
        finally
        {
            await _admin.DeleteAsync($"{MaterialUrl}/{field.Id}");
        }
    }

    [Fact]
    public async Task Material_NumberCustomField_RefusesNonNumericValue()
    {
        var field = await CreateFieldAsync(MaterialUrl, $"Giorni {Guid.NewGuid():N}", CustomFieldType.Number);
        try
        {
            var code = $"MAT-{Guid.NewGuid():N}"[..12];
            var response = await _admin.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(
                code, $"Materiale {code}", "pz", 0, 0,
                new Dictionary<string, string?> { [field.Key] = "non-un-numero" }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await _admin.DeleteAsync($"{MaterialUrl}/{field.Id}");
        }
    }
}
