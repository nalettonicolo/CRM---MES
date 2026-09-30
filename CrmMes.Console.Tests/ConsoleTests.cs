using System.Net;
using System.Net.Http.Json;
using CrmMes.Console.Data;
using CrmMes.Console.Services;
using CrmMes.Core.Security;
using CrmMes.Licensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Console.Tests;

public class SubscriptionRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static Customer Paid(DateTime? paidUntil, int grace = 15, DateTime? created = null, string? forced = null) =>
        new() { Name = "Cliente", PaidUntil = paidUntil, GraceDays = grace, CreatedAt = created ?? Now.AddMonths(-6), ForcedStatus = forced };

    [Fact]
    public void Paid_Late_ThenSuspended()
    {
        Assert.Equal(LicenseStatus.Active, SubscriptionRules.Evaluate(Paid(Now.AddDays(3)), Now).Status);

        var late = SubscriptionRules.Evaluate(Paid(Now.AddDays(-5)), Now);
        Assert.Equal(LicenseStatus.Grace, late.Status);
        Assert.Contains("verrà sospeso", late.Message);

        var suspended = SubscriptionRules.Evaluate(Paid(Now.AddDays(-16)), Now);
        Assert.Equal(LicenseStatus.Suspended, suspended.Status);
        Assert.Contains("mancato pagamento", suspended.Message);
    }

    [Fact]
    public void NewCustomer_HasTheGraceDaysForTheFirstPayment()
    {
        Assert.Equal(LicenseStatus.Grace, SubscriptionRules.Evaluate(Paid(null, created: Now.AddDays(-3)), Now).Status);
        Assert.Equal(LicenseStatus.Suspended, SubscriptionRules.Evaluate(Paid(null, created: Now.AddDays(-20)), Now).Status);
    }

    [Fact]
    public void ManualDecision_OverridesThePayments()
    {
        Assert.Equal(LicenseStatus.Suspended, SubscriptionRules.Evaluate(Paid(Now.AddMonths(2), forced: LicenseStatus.Suspended), Now).Status);
        Assert.Equal(LicenseStatus.Active, SubscriptionRules.Evaluate(Paid(Now.AddMonths(-2), forced: LicenseStatus.Active), Now).Status);
    }

    [Fact]
    public void MonthlyFee_IsPlanPlusModulesPlusExtraUsers()
    {
        var prices = new List<PriceItem>
        {
            new() { Key = "standard", Kind = "plan", MonthlyPrice = 99, IncludedUsers = 10 },
            new() { Key = "user", Kind = "user", MonthlyPrice = 8 },
            new() { Key = "sales", Kind = "module", MonthlyPrice = 15 },
            new() { Key = "invoicing", Kind = "module", MonthlyPrice = 20 },
        };
        var customer = new Customer { PlanKey = "standard", Modules = "sales,invoicing,inesistente", ExtraUsers = 2 };

        Assert.Equal(99 + 15 + 20 + 16, Pricing.MonthlyTotal(customer, prices));
        Assert.Equal(12, Pricing.MaxUsers(customer, prices));
        Assert.Equal(["sales", "invoicing"], Pricing.ParseModules(customer.Modules));
    }
}

public class HeartbeatTests : IAsyncLifetime
{
    private readonly ConsoleFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static Heartbeat Beat() => new("1.7.0", "on-premise", "Officine Aurora srl", "IT01234567890", ["sales"], 7, 50_000_000, true, DateTime.UtcNow);

    private async Task<(Guid CustomerId, string Key)> CustomerWithInstallationAsync(string modules, DateTime? paidUntil, string? forced = null)
    {
        var key = LicenseKeys.New();
        await using var db = _factory.Db();
        var customer = new Customer { Name = "Officine Aurora srl", PlanKey = "standard", Modules = modules, PaidUntil = paidUntil, ForcedStatus = forced };
        customer.Installations.Add(new Installation { Name = "Server sede", KeyHash = LicenseKeys.Hash(key), KeyPrefix = LicenseKeys.Prefix(key) });
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return (customer.Id, key);
    }

    private async Task<HttpResponseMessage> SendAsync(string? key)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/installations/heartbeat") { Content = JsonContent.Create(Beat()) };
        if (key is not null)
        {
            request.Headers.Add(HeartbeatEndpoint.KeyHeader, key);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task ValidKey_GetsASignedLicense_WithPlanModulesAndUsers()
    {
        var (customerId, key) = await CustomerWithInstallationAsync("sales,invoicing", DateTime.UtcNow.AddDays(20));

        var response = await SendAsync(key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reply = (await response.Content.ReadFromJsonAsync<HeartbeatReply>())!;
        var published = await _factory.CreateClient().GetFromJsonAsync<Dictionary<string, string>>("/api/public-key");
        Assert.Equal(published!["publicKey"], reply.PublicKey);
        using var publicKey = LicenseToken.ImportPublicKey(reply.PublicKey);
        var grant = LicenseToken.Verify(reply.License, publicKey)!;
        Assert.Equal(LicenseStatus.Active, grant.Status);
        Assert.Equal("Standard", grant.Plan);
        Assert.Equal(["sales", "invoicing"], grant.Modules);
        Assert.Equal(10, grant.MaxUsers);
        Assert.True(grant.ValidUntil > DateTime.UtcNow.AddDays(29));

        await using var db = _factory.Db();
        var installation = await db.Installations.SingleAsync(i => i.CustomerId == customerId);
        Assert.Equal("1.7.0", installation.Version);
        Assert.Equal(7, installation.ActiveUsers);
        Assert.Equal(LicenseStatus.Active, installation.LastStatusSent);
    }

    [Fact]
    public async Task UnpaidCustomer_GetsASuspendedLicense()
    {
        var (_, key) = await CustomerWithInstallationAsync("sales", DateTime.UtcNow.AddDays(-40));

        var reply = (await (await SendAsync(key)).Content.ReadFromJsonAsync<HeartbeatReply>())!;
        using var publicKey = LicenseToken.ImportPublicKey(reply.PublicKey);

        Assert.Equal(LicenseStatus.Suspended, LicenseToken.Verify(reply.License, publicKey)!.Status);
    }

    [Fact]
    public async Task MissingWrongOrRevokedKeys_AreRefused()
    {
        var (customerId, key) = await CustomerWithInstallationAsync("sales", DateTime.UtcNow.AddDays(20));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync("nmes_chiave-inventata")).StatusCode);

        await using (var db = _factory.Db())
        {
            (await db.Installations.SingleAsync(i => i.CustomerId == customerId)).Revoked = true;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(key)).StatusCode);
    }

    [Fact]
    public async Task SigningKey_SurvivesARestart_EncryptedInTheDatabase()
    {
        var first = (await _factory.CreateClient().GetFromJsonAsync<Dictionary<string, string>>("/api/public-key"))!["publicKey"];

        await using var db = _factory.Db();
        var stored = await db.Settings.SingleAsync(s => s.Key == "license-signing-key");
        Assert.StartsWith("v1:", stored.Value);

        var protector = new SecretProtector("console-test-secret-console-test-secret-0123");
        var keys = await ConsoleKeys.LoadOrCreateAsync(db, protector);
        Assert.Equal(first, keys.PublicKey);
    }
}

public class ConsoleSignInTests : IAsyncLifetime
{
    private readonly ConsoleFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<byte[]> PendingSecretAsync(string email)
    {
        await using var db = _factory.Db();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        return _factory.Services.GetRequiredService<SecretProtector>().Unprotect(user.TotpPendingSecret)!;
    }

    [Fact]
    public async Task Everything_RequiresLogin_AndTwoFactor()
    {
        var browser = _factory.Browser();

        var anonymous = await browser.GetAsync("/Customers");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/Login", anonymous.Headers.Location!.ToString());

        // First start: the vendor's account, then the compulsory authenticator setup.
        var setup = await browser.PostFormAsync("/Setup", new() { ["Name"] = "Nicolò", ["Email"] = "titolare@example.test", ["Password"] = "una-password-lunga-di-prova" });
        Assert.Equal("/Enroll", setup.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Customers")).StatusCode);

        var enrollPage = await browser.GetStringAsync("/Enroll");
        Assert.Contains("data:image/png;base64,", enrollPage);
        var secret = await PendingSecretAsync("titolare@example.test");
        var enrolled = await browser.PostFormAsync("/Enroll", new() { ["Code"] = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) });
        Assert.Equal("/", enrolled.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Customers")).StatusCode);

        // A second visit: password alone is not enough.
        var other = _factory.Browser();
        var password = await other.PostFormAsync("/Login", new() { ["Email"] = "titolare@example.test", ["Password"] = "una-password-lunga-di-prova" });
        Assert.Equal("/LoginCode", password.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await other.GetAsync("/Customers")).StatusCode);
        var wrong = await other.PostFormAsync("/LoginCode", new() { ["Code"] = "000000" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Codice non valido", await wrong.Content.ReadAsStringAsync());

        // Setup is closed once an account exists.
        Assert.Equal(HttpStatusCode.Redirect, (await _factory.Browser().GetAsync("/Setup")).StatusCode);
    }

    [Fact]
    public async Task SecurityHeaders_ForbidFramingAndForeignScripts()
    {
        var response = await _factory.Browser().GetAsync("/Login");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe", csp);
    }
}
