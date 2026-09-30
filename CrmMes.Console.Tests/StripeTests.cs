using System.Net;
using System.Text;
using CrmMes.Console.Data;
using CrmMes.Console.Services;
using CrmMes.Core.Security;
using CrmMes.Licensing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrmMes.Console.Tests;

public sealed class FakeStripe : IStripeGateway
{
    public List<string> Calls { get; } = [];
    public decimal? LastPriceAmount { get; private set; }

    public Task<string> CreateCustomerAsync(string name, string? email, Guid consoleCustomerId, CancellationToken cancellationToken)
    {
        Calls.Add("customer");
        return Task.FromResult("cus_test_1");
    }

    public Task<string> CreateMonthlyPriceAsync(decimal amount, string description, CancellationToken cancellationToken)
    {
        Calls.Add("price");
        LastPriceAmount = amount;
        return Task.FromResult("price_test_" + amount);
    }

    public Task<string> CreateCheckoutAsync(string stripeCustomerId, string priceId, Guid consoleCustomerId, string successUrl, string cancelUrl, CancellationToken cancellationToken)
    {
        Calls.Add("checkout:" + priceId);
        return Task.FromResult("https://checkout.stripe.com/c/pay/test_link");
    }

    public Task ChangeSubscriptionPriceAsync(string subscriptionId, string priceId, CancellationToken cancellationToken)
    {
        Calls.Add($"change:{subscriptionId}:{priceId}");
        return Task.CompletedTask;
    }

    public Task<string> CreatePortalAsync(string stripeCustomerId, string returnUrl, CancellationToken cancellationToken) =>
        Task.FromResult("https://billing.stripe.com/p/session/test");
}

public class StripeSignatureTests
{
    [Fact]
    public void Signature_IsCheckedAndFreshnessEnforced()
    {
        var now = DateTimeOffset.UtcNow;
        var header = $"t={now.ToUnixTimeSeconds()},v1={StripeSignature.Sign("{}", now.ToUnixTimeSeconds(), "whsec_prova")}";

        Assert.True(StripeSignature.IsValid("{}", header, "whsec_prova", now));
        Assert.False(StripeSignature.IsValid("{ }", header, "whsec_prova", now));          // body changed
        Assert.False(StripeSignature.IsValid("{}", header, "whsec_altra", now));           // other secret
        Assert.False(StripeSignature.IsValid("{}", header, "whsec_prova", now.AddMinutes(10))); // replayed later
        Assert.False(StripeSignature.IsValid("{}", null, "whsec_prova", now));
    }
}

public class StripeWebhookTests : IAsyncLifetime
{
    private const string WebhookSecret = "whsec_test_console";
    private readonly FakeStripe _stripe = new();
    private readonly ConsoleFactory _base = new();
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = _base.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Stripe:SecretKey", "sk_test_console");
            builder.UseSetting("Stripe:WebhookSecret", WebhookSecret);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStripeGateway>();
                services.AddSingleton<IStripeGateway>(_stripe);
            });
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _base.DisposeAsync();
    }

    private ConsoleDbContext Db() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<ConsoleDbContext>();

    private async Task<HttpResponseMessage> SendEventAsync(string json, string? secret = WebhookSecret)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", $"t={now},v1={StripeSignature.Sign(json, now, secret ?? "x")}");
        return await _factory.CreateClient().SendAsync(request);
    }

    private async Task<Guid> CustomerAsync(string? stripeCustomer = null, string? subscription = null, DateTime? paidUntil = null)
    {
        await using var db = Db();
        var customer = new Customer { Name = "Officine Aurora srl", StripeCustomerId = stripeCustomer, StripeSubscriptionId = subscription, PaidUntil = paidUntil, PlanKey = "base" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    [Fact]
    public async Task PaidInvoice_IsRecordedAtOnce_AndMovesPaidUntil_OnlyOnce()
    {
        var id = await CustomerAsync("cus_aurora", "sub_aurora", DateTime.UtcNow.AddDays(-3));
        var periodEnd = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var json = """
            {"type":"invoice.paid","data":{"object":{"id":"in_001","number":"A-0001","customer":"cus_aurora","amount_paid":12900,
             "lines":{"data":[{"period":{"start":1,"end":PERIOD_END}}]}}}}
            """.Replace("PERIOD_END", periodEnd.ToString());

        Assert.Equal(HttpStatusCode.OK, (await SendEventAsync(json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendEventAsync(json)).StatusCode); // delivered twice

        await using var db = Db();
        var customer = await db.Customers.Include(c => c.Payments).SingleAsync(c => c.Id == id);
        var payment = Assert.Single(customer.Payments);
        Assert.Equal(129m, payment.Amount);
        Assert.Equal(BillingMethods.Stripe, payment.Method);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(periodEnd).UtcDateTime, customer.PaidUntil);
        Assert.Equal(LicenseStatus.Active, SubscriptionRules.Evaluate(customer, DateTime.UtcNow).Status);
    }

    [Fact]
    public async Task FailedPayment_IsRecorded_ButDoesNotSuspendByItself()
    {
        var id = await CustomerAsync("cus_late", "sub_late", DateTime.UtcNow.AddDays(-2));

        await SendEventAsync("""{"type":"invoice.payment_failed","data":{"object":{"id":"in_002","customer":"cus_late","amount_due":4900}}}""");

        await using var db = Db();
        var customer = await db.Customers.Include(c => c.Payments).SingleAsync(c => c.Id == id);
        Assert.Equal("failed", Assert.Single(customer.Payments).Status);
        Assert.Equal(LicenseStatus.Grace, SubscriptionRules.Evaluate(customer, DateTime.UtcNow).Status);
    }

    [Fact]
    public async Task CompletedCheckout_LinksTheSubscription()
    {
        var id = await CustomerAsync();

        await SendEventAsync("""{"type":"checkout.session.completed","data":{"object":{"customer":"cus_new","subscription":"sub_new","metadata":{"console_customer_id":"CUSTOMER_ID"}}}}""".Replace("CUSTOMER_ID", id.ToString()));

        await using var db = Db();
        var customer = await db.Customers.SingleAsync(c => c.Id == id);
        Assert.Equal("cus_new", customer.StripeCustomerId);
        Assert.Equal("sub_new", customer.StripeSubscriptionId);
    }

    [Fact]
    public async Task ForgedOrMissingSignature_IsRefused()
    {
        await CustomerAsync("cus_x", "sub_x");

        var forged = await SendEventAsync("""{"type":"invoice.paid","data":{"object":{"id":"in_9","customer":"cus_x","amount_paid":100}}}""", secret: "whsec_sbagliata");

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        await using var db = Db();
        Assert.Empty(await db.Payments.ToListAsync());
    }

    [Fact]
    public async Task ChangingModules_ChangesTheStripePrice_AndCheckoutLinkUsesTheFee()
    {
        var id = await CustomerAsync("cus_aurora", "sub_aurora", DateTime.UtcNow.AddDays(20));
        var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await SignInAsync(browser);

        var page = $"/Customers/Details/{id}";
        var saved = await browser.PostFormAsync(page, new() { ["PlanKey"] = "standard", ["Modules"] = "sales", ["ExtraUsers"] = "0" }, page + "?handler=Subscription");

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("anche su Stripe", await saved.Content.ReadAsStringAsync());
        Assert.Equal(99m + 15m, _stripe.LastPriceAmount);
        Assert.Contains(_stripe.Calls, c => c.StartsWith("change:sub_aurora:price_test_114"));
    }

    private async Task SignInAsync(HttpClient browser)
    {
        await browser.PostFormAsync("/Setup", new() { ["Name"] = "Titolare", ["Email"] = "t@example.test", ["Password"] = "una-password-lunga-di-prova" });
        await browser.GetStringAsync("/Enroll"); // creates the secret to set up
        await using var db = Db();
        var user = await db.Users.SingleAsync();
        var secret = _factory.Services.GetRequiredService<SecretProtector>().Unprotect(user.TotpPendingSecret)!;
        await browser.PostFormAsync("/Enroll", new() { ["Code"] = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) });
    }
}
