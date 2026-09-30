using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrmMes.Console.Data;
using Microsoft.EntityFrameworkCore;

namespace CrmMes.Console.Services;

/// <summary>Stripe keys from the environment (STRIPE_SECRET_KEY, STRIPE_WEBHOOK_SECRET) or configuration.
/// Without them the console works with bank transfers only and says so.</summary>
public sealed class StripeOptions
{
    public StripeOptions(IConfiguration configuration)
    {
        SecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? configuration["Stripe:SecretKey"];
        WebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET") ?? configuration["Stripe:WebhookSecret"];
    }

    public string? SecretKey { get; }
    public string? WebhookSecret { get; }
    public bool Enabled => !string.IsNullOrWhiteSpace(SecretKey);
    public bool IsTestMode => SecretKey?.StartsWith("sk_test_", StringComparison.Ordinal) == true;
}

/// <summary>The few Stripe operations the console needs. One monthly price per customer (its fee), so any
/// change of modules or users is a change of price on the same subscription.</summary>
public interface IStripeGateway
{
    Task<string> CreateCustomerAsync(string name, string? email, Guid consoleCustomerId, CancellationToken cancellationToken);
    Task<string> CreateMonthlyPriceAsync(decimal amount, string description, CancellationToken cancellationToken);
    Task<string> CreateCheckoutAsync(string stripeCustomerId, string priceId, Guid consoleCustomerId, string successUrl, string cancelUrl, CancellationToken cancellationToken);
    Task ChangeSubscriptionPriceAsync(string subscriptionId, string priceId, CancellationToken cancellationToken);
    Task<string> CreatePortalAsync(string stripeCustomerId, string returnUrl, CancellationToken cancellationToken);
}

/// <summary>Stripe's REST API with plain form posts (no SDK: nothing to break when their library changes).</summary>
public sealed class StripeHttpGateway : IStripeGateway
{
    public const string HttpClientName = "stripe";
    private const string ProductSetting = "stripe-product-id";
    private readonly IHttpClientFactory _http;
    private readonly StripeOptions _options;
    private readonly IServiceScopeFactory _scopes;

    public StripeHttpGateway(IHttpClientFactory http, StripeOptions options, IServiceScopeFactory scopes)
    {
        _http = http;
        _options = options;
        _scopes = scopes;
    }

    public async Task<string> CreateCustomerAsync(string name, string? email, Guid consoleCustomerId, CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>> { new("name", name), new("metadata[console_customer_id]", consoleCustomerId.ToString()), new("preferred_locales[0]", "it") };
        if (!string.IsNullOrWhiteSpace(email)) fields.Add(new("email", email));
        return (await PostAsync("customers", fields, cancellationToken)).GetProperty("id").GetString()!;
    }

    public async Task<string> CreateMonthlyPriceAsync(decimal amount, string description, CancellationToken cancellationToken)
    {
        var product = await ProductIdAsync(cancellationToken);
        var price = await PostAsync("prices",
        [
            new("product", product),
            new("currency", "eur"),
            new("unit_amount", ((long)Math.Round(amount * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)),
            new("recurring[interval]", "month"),
            new("nickname", description),
        ], cancellationToken);
        return price.GetProperty("id").GetString()!;
    }

    public async Task<string> CreateCheckoutAsync(string stripeCustomerId, string priceId, Guid consoleCustomerId, string successUrl, string cancelUrl, CancellationToken cancellationToken)
    {
        var session = await PostAsync("checkout/sessions",
        [
            new("mode", "subscription"),
            new("customer", stripeCustomerId),
            new("line_items[0][price]", priceId),
            new("line_items[0][quantity]", "1"),
            new("success_url", successUrl),
            new("cancel_url", cancelUrl),
            new("locale", "it"),
            new("metadata[console_customer_id]", consoleCustomerId.ToString()),
            new("subscription_data[metadata][console_customer_id]", consoleCustomerId.ToString()),
        ], cancellationToken);
        return session.GetProperty("url").GetString()!;
    }

    public async Task ChangeSubscriptionPriceAsync(string subscriptionId, string priceId, CancellationToken cancellationToken)
    {
        var subscription = await GetAsync($"subscriptions/{subscriptionId}", cancellationToken);
        var itemId = subscription.GetProperty("items").GetProperty("data")[0].GetProperty("id").GetString()!;
        await PostAsync($"subscriptions/{subscriptionId}",
        [
            new("items[0][id]", itemId),
            new("items[0][price]", priceId),
            new("proration_behavior", "create_prorations"),
        ], cancellationToken);
    }

    public async Task<string> CreatePortalAsync(string stripeCustomerId, string returnUrl, CancellationToken cancellationToken) =>
        (await PostAsync("billing_portal/sessions", [new("customer", stripeCustomerId), new("return_url", returnUrl)], cancellationToken))
            .GetProperty("url").GetString()!;

    /// <summary>One product "Abbonamento Nicolò MES" for every customer's price, created once.</summary>
    private async Task<string> ProductIdAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsoleDbContext>();
        var stored = await db.Settings.SingleOrDefaultAsync(s => s.Key == ProductSetting, cancellationToken);
        if (stored is not null)
        {
            return stored.Value;
        }

        var product = await PostAsync("products", [new("name", "Abbonamento Nicolò MES")], cancellationToken);
        var id = product.GetProperty("id").GetString()!;
        db.Settings.Add(new ConsoleSetting { Key = ProductSetting, Value = id });
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    private async Task<JsonElement> PostAsync(string path, List<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/" + path) { Content = new FormUrlEncodedContent(fields) };
        return await SendAsync(request, cancellationToken);
    }

    private async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.stripe.com/v1/" + path);
        return await SendAsync(request, cancellationToken);
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Stripe non è collegato: imposta STRIPE_SECRET_KEY.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);
        using var response = await _http.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = JsonDocument.Parse(body).RootElement.Clone();
        if (!response.IsSuccessStatusCode)
        {
            var message = json.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var text) ? text.GetString() : body;
            throw new InvalidOperationException($"Stripe ha rifiutato l'operazione: {message}");
        }

        return json;
    }
}

/// <summary>Stripe's webhook signature: header "t=timestamp,v1=hex(HMAC-SHA256(secret, timestamp + '.' + body))",
/// refused when older than five minutes (a replayed event is not accepted).</summary>
public static class StripeSignature
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static bool IsValid(string payload, string? header, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t" && long.TryParse(kv[1], out var t)) timestamp = t;
            if (kv[0] == "v1") signatures.Add(kv[1]);
        }

        if (timestamp is null || signatures.Count == 0 || (now - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value)).Duration() > Tolerance)
        {
            return false;
        }

        var expected = Sign(payload, timestamp.Value, secret);
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), Encoding.ASCII.GetBytes(expected)));
    }

    public static string Sign(string payload, long timestamp, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
}

/// <summary>What Stripe tells the console: a checkout completed (customer and subscription linked), an invoice
/// paid (payment recorded at once, paid through the end of its period), an invoice failed (recorded; the
/// grace days then run from PaidUntil), a subscription cancelled.</summary>
public static class StripeWebhookEndpoint
{
    public static async Task<IResult> HandleAsync(HttpRequest request, ConsoleDbContext db, StripeOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.WebhookSecret))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        if (!StripeSignature.IsValid(payload, request.Headers["Stripe-Signature"], options.WebhookSecret, DateTimeOffset.UtcNow))
        {
            return Results.BadRequest(new { message = "Firma Stripe non valida." });
        }

        using var document = JsonDocument.Parse(payload);
        var type = document.RootElement.GetProperty("type").GetString();
        var data = document.RootElement.GetProperty("data").GetProperty("object");
        switch (type)
        {
            case "checkout.session.completed":
                await LinkCheckoutAsync(db, data, cancellationToken);
                break;
            case "invoice.paid":
                await RecordInvoiceAsync(db, data, paid: true, cancellationToken);
                break;
            case "invoice.payment_failed":
                await RecordInvoiceAsync(db, data, paid: false, cancellationToken);
                break;
            case "customer.subscription.deleted":
                var subscriptionId = data.GetProperty("id").GetString();
                var customer = await db.Customers.SingleOrDefaultAsync(c => c.StripeSubscriptionId == subscriptionId, cancellationToken);
                if (customer is not null)
                {
                    customer.StripeSubscriptionId = null;
                    db.Audit.Add(new ConsoleAudit { User = "Stripe", Action = "SubscriptionCancelled", Details = customer.Name });
                    await db.SaveChangesAsync(cancellationToken);
                }

                break;
        }

        return Results.Ok();
    }

    private static async Task LinkCheckoutAsync(ConsoleDbContext db, JsonElement session, CancellationToken cancellationToken)
    {
        if (!session.TryGetProperty("metadata", out var metadata) || !metadata.TryGetProperty("console_customer_id", out var idElement)
            || !Guid.TryParse(idElement.GetString(), out var consoleId))
        {
            return;
        }

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == consoleId, cancellationToken);
        if (customer is null)
        {
            return;
        }

        customer.StripeCustomerId = Str(session, "customer") ?? customer.StripeCustomerId;
        customer.StripeSubscriptionId = Str(session, "subscription") ?? customer.StripeSubscriptionId;
        customer.BillingMethod = BillingMethods.Stripe;
        db.Audit.Add(new ConsoleAudit { User = "Stripe", Action = "CheckoutCompleted", Details = customer.Name });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RecordInvoiceAsync(ConsoleDbContext db, JsonElement invoice, bool paid, CancellationToken cancellationToken)
    {
        var invoiceId = Str(invoice, "id");
        var stripeCustomer = Str(invoice, "customer");
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.StripeCustomerId == stripeCustomer, cancellationToken);
        if (customer is null || invoiceId is null)
        {
            return;
        }

        var status = paid ? "paid" : "failed";
        if (await db.Payments.AnyAsync(p => p.StripeInvoiceId == invoiceId && p.Status == status, cancellationToken))
        {
            return; // Stripe may deliver the same event more than once
        }

        DateTime? periodEnd = null;
        if (invoice.TryGetProperty("lines", out var lines) && lines.TryGetProperty("data", out var items) && items.GetArrayLength() > 0
            && items[0].TryGetProperty("period", out var period) && period.TryGetProperty("end", out var end) && end.TryGetInt64(out var endSeconds))
        {
            periodEnd = DateTimeOffset.FromUnixTimeSeconds(endSeconds).UtcDateTime;
        }

        var cents = invoice.TryGetProperty(paid ? "amount_paid" : "amount_due", out var amount) && amount.TryGetInt64(out var value) ? value : 0;
        db.Payments.Add(new Payment
        {
            CustomerId = customer.Id,
            Amount = cents / 100m,
            PaidAt = DateTime.UtcNow,
            Method = BillingMethods.Stripe,
            StripeInvoiceId = invoiceId,
            Status = status,
            PeriodEnd = periodEnd,
            RecordedBy = "Stripe",
            Reference = Str(invoice, "number"),
        });
        if (paid && periodEnd is not null && (customer.PaidUntil is null || periodEnd > customer.PaidUntil))
        {
            customer.PaidUntil = periodEnd;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
