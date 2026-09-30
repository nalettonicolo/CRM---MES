using System.Threading.RateLimiting;
using CrmMes.Console.Data;
using CrmMes.Console.Services;
using CrmMes.Core.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Vendor console of Nicolò MES: customers, installations, subscriptions and payments, license issuing.
// A separate service with its own database; the customers' installations only reach its heartbeat endpoint.
var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("CONSOLE_DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("Console")
    ?? throw new InvalidOperationException("Database della console non configurato: imposta CONSOLE_DATABASE_URL.");
var secret = Environment.GetEnvironmentVariable("CONSOLE_SECRET")
    ?? builder.Configuration["Console:Secret"]
    ?? (builder.Environment.IsDevelopment() ? "development-only-console-secret-change-me-32chars" : null);
if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
{
    throw new InvalidOperationException("Imposta CONSOLE_SECRET con almeno 32 caratteri: protegge la chiave di firma delle licenze.");
}

builder.Services.AddDbContext<ConsoleDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(new SecretProtector(secret));
builder.Services.AddSingleton<ConsoleKeyStore>();
builder.Services.AddSingleton<StripeOptions>();
builder.Services.AddSingleton<IStripeGateway, StripeHttpGateway>();
builder.Services.AddHttpClient(StripeHttpGateway.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<IPasswordHasher<ConsoleUser>, PasswordHasher<ConsoleUser>>();
builder.Services.AddDataProtection().PersistKeysToDbContext<ConsoleDbContext>().SetApplicationName("nicolomes-console");

builder.Services.AddAuthentication(ConsoleAuth.Scheme).AddCookie(options =>
{
    options.LoginPath = "/Login";
    options.AccessDeniedPath = "/Login";
    options.Cookie.Name = "nicolomes.console";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = false;
});
builder.Services.AddAuthorization(options =>
{
    // Every page and endpoint that doesn't say otherwise requires a completed sign-in (password + code):
    // a page added tomorrow is protected without anyone remembering to protect it.
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireClaim(ConsoleAuth.StageClaim, ConsoleAuth.StageFull).Build();
    options.AddPolicy(ConsoleAuth.FullPolicy, p => p.RequireClaim(ConsoleAuth.StageClaim, ConsoleAuth.StageFull));
    options.AddPolicy(ConsoleAuth.EnrollPolicy, p => p.RequireClaim(ConsoleAuth.StageClaim, ConsoleAuth.StageEnroll, ConsoleAuth.StageFull));
    options.AddPolicy(ConsoleAuth.CodePolicy, p => p.RequireClaim(ConsoleAuth.StageClaim, ConsoleAuth.StageCode));
});
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Setup");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AuthorizePage("/LoginCode", ConsoleAuth.CodePolicy);
    options.Conventions.AuthorizePage("/Enroll", ConsoleAuth.EnrollPolicy);
    options.Conventions.AuthorizePage("/Logout");
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:LoginPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("heartbeat", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:HeartbeatPerMinute", 60), Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ConsoleDbContext>();
    if (app.Configuration.GetValue("Database:AutoMigrate", true))
    {
        await db.Database.MigrateAsync();
    }
    else if (app.Configuration.GetValue<bool>("Database:EnsureCreated"))
    {
        await db.Database.EnsureCreatedAsync();
    }

    await Pricing.SeedAsync(db);
}

app.UseForwardedHeaders();
app.UseExceptionHandler("/Error");
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; form-action 'self'; base-uri 'self'; frame-ancestors 'none'";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "DENY";
    headers["Cache-Control"] = "no-store";
    await next();
});
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/installations/heartbeat", HeartbeatEndpoint.HandleAsync).RequireRateLimiting("heartbeat").AllowAnonymous();
app.MapPost("/api/installations/support", SupportEndpoint.CreateAsync).RequireRateLimiting("heartbeat").AllowAnonymous();
app.MapGet("/api/installations/support", SupportEndpoint.ListAsync).RequireRateLimiting("heartbeat").AllowAnonymous();
app.MapPost("/api/stripe/webhook", StripeWebhookEndpoint.HandleAsync).AllowAnonymous();
app.MapGet("/api/public-key", async (ConsoleKeyStore keys, CancellationToken ct) => Results.Ok(new { publicKey = (await keys.GetAsync(ct)).PublicKey }))
    .AllowAnonymous();
app.MapGet("/health", async (ConsoleDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503))
    .AllowAnonymous();
app.MapRazorPages();

app.Run();

public partial class Program;
