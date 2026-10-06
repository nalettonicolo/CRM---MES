using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Layout;

namespace CrmMes.Api.Tests;

public class FormLayoutTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;
    private const string Url = "/api/layout/" + FormLayoutRegistry.ServiceRequest;

    public FormLayoutTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static SaveLayoutFieldRequest Field(string key, string? label, int order, bool visible = true, bool required = false) =>
        new(key, label, order, visible, required);

    [Fact]
    public async Task Defaults_AreReturnedInOrder_WhenNothingIsCustomised()
    {
        // La base dati è condivisa tra i test: si riporta la schermata ai default prima di controllarli.
        await _admin.PutAsJsonAsync(Url, new SaveLayoutRequest([]));
        var screen = await _admin.GetFromJsonAsync<LayoutScreenResponse>("/api/layout/service.request");

        Assert.NotNull(screen);
        Assert.Equal(FormLayoutRegistry.Screens[FormLayoutRegistry.ServiceRequest].Count, screen!.Fields.Count);
        Assert.Equal("Nuova richiesta di assistenza (Service)", screen.Name);
        Assert.Equal("subject", screen.Fields[0].Key);
        Assert.Equal("Oggetto", screen.Fields[0].Label);
        Assert.True(screen.Fields[0].DefaultRequired);
        Assert.False(screen.Fields[0].CanHide);
    }

    [Fact]
    public async Task Admin_CanRelabelReorderHideAndRequire_AndTheChangesAreKept()
    {
        var fields = new List<SaveLayoutFieldRequest>
        {
            Field("description", "Dettaglio del guasto", 1, required: true),
            Field("subject", "Titolo breve", 2),
            Field("priority", "Priorità", 3),
            Field("channel", "Come ci hai contattato", 4),
            Field("requestedBy", "Chiamante", 5, visible: false),
            Field("contactInfo", null, 6),
        };

        var saved = await _admin.PutAsJsonAsync(Url, new SaveLayoutRequest(fields));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var screen = await _admin.GetFromJsonAsync<LayoutScreenResponse>(Url);
        Assert.Equal("description", screen!.Fields[0].Key);
        Assert.Equal("Dettaglio del guasto", screen.Fields[0].Label);
        Assert.True(screen.Fields[0].Required);
        Assert.Equal("Titolo breve", screen.Fields.Single(f => f.Key == "subject").Label);
        Assert.False(screen.Fields.Single(f => f.Key == "requestedBy").Visible);
        // Etichetta svuotata: torna il default del codice.
        Assert.Equal("Contatto", screen.Fields.Single(f => f.Key == "contactInfo").Label);
    }

    [Fact]
    public async Task ProtectedFields_CannotBeHiddenAndSubjectStaysRequired()
    {
        var fields = new List<SaveLayoutFieldRequest>
        {
            Field("subject", "Oggetto", 1, visible: false, required: false),
            Field("priority", null, 2),
            Field("channel", null, 3),
        };

        await _admin.PutAsJsonAsync(Url, new SaveLayoutRequest(fields));

        var subject = (await _admin.GetFromJsonAsync<LayoutScreenResponse>(Url))!.Fields.Single(f => f.Key == "subject");
        Assert.True(subject.Visible);
        Assert.True(subject.Required);
    }

    [Fact]
    public async Task UnknownField_Duplicate_AndUnknownScreen_AreRefused()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync(Url,
            new SaveLayoutRequest([Field("inventato", null, 1)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync(Url,
            new SaveLayoutRequest([Field("subject", null, 1), Field("subject", null, 2)]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/api/layout/altra.schermata")).StatusCode);
    }

    [Fact]
    public async Task OnlyAdmin_CanChangeTheLayout_EveryoneCanRead()
    {
        var operator_ = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var client = _fixture.Factory.AuthenticatedClient(operator_.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(Url, new SaveLayoutRequest([Field("subject", null, 1)]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
    }
}
