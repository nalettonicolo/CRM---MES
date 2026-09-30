using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using CrmMes.Licensing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

/// <summary>Stands in for the vendor console: checks the license key, answers with a license signed by its
/// key, as configured by the test (status, modules, users).</summary>
public sealed class FakeLicenseConsole : HttpMessageHandler
{
    public ECDsa Key { get; set; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public string Status { get; set; } = LicenseStatus.Active;
    public List<string> Modules { get; set; } = ["sales", "purchasing"];
    public int? MaxUsers { get; set; }
    public Heartbeat? LastHeartbeat { get; private set; }
    public string ExpectedKey { get; } = "chiave-licenza-di-prova";

    public SupportRequest? LastSupportRequest { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath == "/api/installations/support"
            && request.Headers.TryGetValues(LicenseHeartbeatService.KeyHeader, out var supportKeys) && supportKeys.Single() == ExpectedKey)
        {
            if (request.Method == HttpMethod.Post)
            {
                LastSupportRequest = await request.Content!.ReadFromJsonAsync<SupportRequest>(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new SupportTicketInfo(Guid.NewGuid(), 42, LastSupportRequest!.Subject, SupportTicketStatus.Open, DateTime.UtcNow, LastSupportRequest.RequestedBy, null, null)),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new SupportTicketInfo(Guid.NewGuid(), 41, "Precedente", SupportTicketStatus.Closed, DateTime.UtcNow.AddDays(-2), "Admin", "Risolto.", DateTime.UtcNow) }),
            };
        }

        if (request.RequestUri!.AbsolutePath != "/api/installations/heartbeat"
            || !request.Headers.TryGetValues(LicenseHeartbeatService.KeyHeader, out var keys) || keys.Single() != ExpectedKey)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        LastHeartbeat = await request.Content!.ReadFromJsonAsync<Heartbeat>(cancellationToken);
        var grant = new LicenseGrant(Guid.NewGuid(), "Officine Aurora srl", "Standard", Modules, MaxUsers, Status,
            Status == LicenseStatus.Suspended ? "Abbonamento sospeso per mancato pagamento." : null,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(30));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new HeartbeatReply(LicenseToken.Sign(grant, Key), LicenseToken.ExportPublicKey(Key))),
        };
    }
}

public class LicenseTokenTests
{
    [Fact]
    public void SignedLicense_CantBeEdited()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var grant = new LicenseGrant(Guid.NewGuid(), "Cliente", "Base", ["sales"], 5, LicenseStatus.Active, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(30));
        var token = LicenseToken.Sign(grant, key);
        using var publicKey = LicenseToken.ImportPublicKey(LicenseToken.ExportPublicKey(key));

        Assert.Equal(["sales"], LicenseToken.Verify(token, publicKey)!.Modules);

        // Someone adds "invoicing" to the payload: the signature no longer matches.
        var parts = token.Split('.');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[0].Replace('-', '+').Replace('_', '/').PadRight((parts[0].Length + 3) / 4 * 4, '=')));
        var forged = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json.Replace("[\"sales\"]", "[\"sales\",\"invoicing\"]")))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + parts[1];
        Assert.Null(LicenseToken.Verify(forged, publicKey));

        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Null(LicenseToken.Verify(token, otherKey));
    }

    [Fact]
    public void ALicenseNotRenewedForTooLong_CountsAsSuspended()
    {
        var grant = new LicenseGrant(Guid.NewGuid(), "Cliente", "Base", [], null, LicenseStatus.Active, null,
            DateTime.UtcNow.AddDays(-40), DateTime.UtcNow.AddDays(-10));

        var (status, message) = LicenseToken.Effective(grant, DateTime.UtcNow);

        Assert.Equal(LicenseStatus.Suspended, status);
        Assert.Contains("verificare l'abbonamento", message);
    }

    [Fact]
    public void NoLicenseYet_WorksWithAWarning()
    {
        var snapshot = LicenseState.Evaluate(null, null, null, DateTime.UtcNow);

        Assert.Equal(LicenseStatus.Grace, snapshot.Status);
        Assert.False(snapshot.IsSuspended);
        Assert.Equal(["sales", "haccp"], snapshot.Allow(["sales", "haccp"]));
    }
}

public class LicensingTests : IAsyncLifetime
{
    private readonly FakeLicenseConsole _console = new();
    private CustomWebApplicationFactory _base = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _base = new CustomWebApplicationFactory();
        _factory = _base.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("License:ConsoleUrl", "https://console.example.test");
            builder.UseSetting("License:Key", _console.ExpectedKey);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(LicenseHeartbeatService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _console));
        });

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
        }

        var client = _factory.CreateClient();
        var auth = await TestAuth.RegisterAsync(client, "Admin", "admin@licenza.test", "AdminPass123!");
        _admin = _factory.CreateClient();
        _admin.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);
        (await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Officine Aurora srl", "IT01234567890", null, null, null, "generic", null))).EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _base.DisposeAsync();
    }

    private async Task CheckAsync()
    {
        var response = await _admin.PostAsync("/api/license/check", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_SendsOnlyTechnicalData_AndTheLicenseLimitsTheModules()
    {
        await CheckAsync();

        var heartbeat = _console.LastHeartbeat!;
        Assert.Equal("Officine Aurora srl", heartbeat.CompanyName);
        Assert.Equal(1, heartbeat.ActiveUsers);
        var license = await _admin.GetFromJsonAsync<LicenseResponse>("/api/license");
        Assert.Equal(LicenseStatus.Active, license!.Status);
        Assert.Equal("Standard", license.Plan);

        var profile = await _admin.GetFromJsonAsync<CompanyProfileResponse>("/api/company-profile");
        Assert.Equal(["sales", "purchasing"], profile!.EnabledModules.OrderByDescending(m => m));

        var notIncluded = await _admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Officine Aurora srl", null, null, null, null, "generic", ["sales", "invoicing"]));
        Assert.Equal(HttpStatusCode.BadRequest, notIncluded.StatusCode);
        Assert.Contains("Fattura elettronica", await notIncluded.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Suspended_LeavesOnlyALimitedOverallView()
    {
        _console.Status = LicenseStatus.Suspended;
        await CheckAsync();

        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync("/api/work-orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync("/api/work-orders/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync("/api/materials")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync("/api/support/info")).StatusCode);

        var detail = await _admin.GetAsync($"/api/work-orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.PaymentRequired, detail.StatusCode);
        Assert.Contains("mancato pagamento", await detail.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.PaymentRequired, (await _admin.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired,
            (await _admin.PostAsJsonAsync("/api/materials", new { code = "X1", name = "Prova", unit = "pz", stock = 0, minStock = 0 })).StatusCode);

        // Paid again: everything back at the next check.
        _console.Status = LicenseStatus.Active;
        await CheckAsync();
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync("/api/customers")).StatusCode);
    }

    [Fact]
    public async Task ALicenseSignedByAnotherKey_IsRefused()
    {
        await CheckAsync();

        _console.Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _console.Status = LicenseStatus.Active;
        _console.Modules = ["sales", "purchasing", "invoicing", "haccp"];
        var response = await _admin.PostAsync("/api/license/check", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var profile = await _admin.GetFromJsonAsync<CompanyProfileResponse>("/api/company-profile");
        Assert.DoesNotContain("invoicing", profile!.EnabledModules);
    }

    [Fact]
    public async Task SupportRequest_GoesToTheConsole_WithTheServersState()
    {
        var response = await _admin.PostAsJsonAsync("/api/support/requests",
            new NewSupportRequest("Stampa DDT", "Il PDF esce vuoto", "333 1234567", "123 456 789"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<SupportTicketInfo>();
        Assert.Equal(42, ticket!.Number);
        var sent = _console.LastSupportRequest!;
        Assert.Contains("admin@licenza.test", sent.RequestedBy);
        Assert.Equal("123 456 789", sent.RemoteSessionId);
        Assert.True(sent.Diagnostics!.Value.GetProperty("database").GetProperty("canConnect").GetBoolean());
        Assert.DoesNotContain("Password", sent.Diagnostics.Value.GetRawText());

        var list = await _admin.GetFromJsonAsync<List<SupportTicketInfo>>("/api/support/requests");
        Assert.Equal("Risolto.", list!.Single().Reply);

        var empty = await _admin.PostAsJsonAsync("/api/support/requests", new NewSupportRequest(" ", "", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task PlanUsers_AreALimit()
    {
        _console.MaxUsers = 2;
        await CheckAsync();

        var first = await _admin.PostAsJsonAsync("/api/users", new CreateUserRequest("Uno", "uno@licenza.test", "TestPass123!", "Operator"));
        var second = await _admin.PostAsJsonAsync("/api/users", new CreateUserRequest("Due", "due@licenza.test", "TestPass123!", "Operator"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("2 utenti", await second.Content.ReadAsStringAsync());
    }
}

public class ModuleCatalogAlignmentTests
{
    /// <summary>The vendor console sells the modules by these keys: a module added to the management software
    /// and forgotten in the catalog (or vice versa) would be impossible to sell or impossible to switch on.</summary>
    [Fact]
    public void ConsoleCatalog_MatchesTheManagementSoftwareModules() =>
        Assert.Equal(
            Sectors.Modules.Select(m => m.Key).OrderBy(k => k),
            ModuleCatalog.All.Select(m => m.Key).OrderBy(k => k));
}
