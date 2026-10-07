using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

// Server installed at the customer's premises: its settings (database, signing key, listening address,
// log folder, support contacts) live in one file outside the program folder, see ServerConfiguration.
var serverConfigPath = CrmMes.Api.Services.ServerConfiguration.ResolvePath();
var serverConfigLoaded = File.Exists(serverConfigPath);
if (serverConfigLoaded)
{
    builder.Configuration.AddJsonFile(serverConfigPath, optional: false, reloadOnChange: false);
}
builder.Configuration[CrmMes.Api.Controllers.SupportController.ServerConfigLoadedKey] = serverConfigLoaded.ToString();

// Runs as a Windows service on a customer's server (no effect when started any other way).
builder.Host.UseWindowsService();

// Structured JSON to console always (readable in Render's log viewer); also ships to Grafana Cloud
// Loki when LOKI_URL is configured (via appsettings, user-secrets locally, or Render env vars) — the
// API runs fine without it, this is purely additive for log retention/search beyond Render's own window.
builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        // ReadFrom.Configuration only picks up a "Serilog" config section, which this project doesn't
        // have (it uses the built-in ASP.NET Core "Logging:LogLevel" schema instead) — without this,
        // ASP.NET Core's own per-request diagnostics (start/end, endpoint matching, status code) log at
        // Information and double up with the single-line request summary below.
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "CrmMes.Api")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
        .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter());

    // A Windows service has no console anyone reads: on a customer's server logs go to daily files,
    // kept 30 days, which the support diagnostics point to.
    var logsPath = context.Configuration["Logs:Path"];
    if (!string.IsNullOrWhiteSpace(logsPath))
    {
        loggerConfiguration.WriteTo.File(
            Path.Combine(logsPath, "api-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
    }

    var lokiUrl = context.Configuration["Loki:Url"] ?? Environment.GetEnvironmentVariable("LOKI_URL");
    if (!string.IsNullOrWhiteSpace(lokiUrl))
    {
        var lokiUser = context.Configuration["Loki:User"] ?? Environment.GetEnvironmentVariable("LOKI_USER");
        var lokiPassword = context.Configuration["Loki:Password"] ?? Environment.GetEnvironmentVariable("LOKI_PASSWORD");
        var credentials = !string.IsNullOrWhiteSpace(lokiUser) && !string.IsNullOrWhiteSpace(lokiPassword)
            ? new LokiCredentials { Login = lokiUser, Password = lokiPassword }
            : null;

        loggerConfiguration.WriteTo.GrafanaLoki(
            lokiUrl,
            credentials: credentials,
            labels: [new LokiLabel { Key = "app", Value = "crmmes-api" }]);
    }
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Standard ASP.NET Core health check framework, additive to the existing hand-rolled /health and
// /api/health/status (left untouched — the desktop client already polls /health during its startup
// retry, and changing its response shape is not worth the risk). This one is for external monitoring
// tooling (uptime checks, orchestrators) that expects the standard health-check JSON shape and wants
// each dependency broken out individually rather than a single combined status.
builder.Services.AddHealthChecks()
    .AddCheck<CrmMes.Api.Services.DatabaseHealthCheck>("postgres");
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (builder.Environment.IsDevelopment() && context.Exception is not null)
        {
            context.ProblemDetails.Detail = context.Exception.ToString();
        }
    };
});
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? Environment.GetEnvironmentVariable("CRM_MES_JWT_KEY")
    ?? (builder.Environment.IsDevelopment() ? "development-only-key-change-before-deploy-32chars" : null);

if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException("Configurare Jwt:Key o CRM_MES_JWT_KEY con almeno 32 caratteri.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("Warehouse", policy => policy.RequireRole("Admin", "Warehouse"));
    options.AddPolicy("Purchasing", policy => policy.RequireRole("Admin", "Purchasing"));
    options.AddPolicy("PurchasingOrWarehouse", policy => policy.RequireRole("Admin", "Purchasing", "Warehouse"));
    options.AddPolicy("PurchasingOrSales", policy => policy.RequireRole("Admin", "Purchasing", "Sales", "Management"));
    // Commercial office: customers and quotes. Converting an accepted quote creates work orders, which
    // warehouse/production staff may also do, hence the combined policy for that one action.
    options.AddPolicy("Sales", policy => policy.RequireRole("Admin", "Sales"));
    options.AddPolicy("SalesOrWarehouse", policy => policy.RequireRole("Admin", "Sales", "Warehouse"));
    // Transport documents leave with sales (Sales/Warehouse) and with goods sent to subcontractors (Purchasing).
    options.AddPolicy("TransportDocuments", policy => policy.RequireRole("Admin", "Sales", "Warehouse", "Purchasing"));
    // Costs, hourly rates and margins: company-confidential, visible only to management.
    // Engineering office: technical documents, product revisions and engineering changes.
    options.AddPolicy("Engineering", policy => policy.RequireRole("Admin", "Management"));
    options.AddPolicy("Service", policy => policy.RequireRole("Admin", "Management", "Sales"));
    options.AddPolicy("Energy", policy => policy.RequireRole("Admin", "Management"));
    options.AddPolicy("ViewMargins", policy => policy.RequireRole(CrmMes.Api.Services.MarginAccess.Roles));
});
builder.Services.AddSingleton<IPasswordHasher<CrmMes.Core.Models.User>, PasswordHasher<CrmMes.Core.Models.User>>();
builder.Services.AddSingleton(new CrmMes.Core.Security.SecretProtector(jwtKey));
builder.Services.AddSingleton(new CrmMes.Api.Services.TwoFactorChallenges(jwtKey));

// Render terminates TLS on its proxy: without this every request would look like it came from the
// proxy's address, and the per-IP rate limit below would become one shared global bucket. ForwardLimit=1
// takes only the address appended by the proxy itself, not whatever a client wrote into the header.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                               Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
// Default cap for any request body that doesn't declare its own [RequestSizeLimit] — plain JSON
// endpoints (create/update DTOs) have no business receiving more than a few hundred KB. The file-upload
// endpoints (catalog import, documents, product images...) already set their own higher limit via
// [RequestSizeLimit], which takes precedence over this default, so they are unaffected.
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 10_000_000;
});
CrmMes.Api.Services.RateLimits.Add(builder.Services, builder.Configuration);
builder.Services.AddScoped<CrmMes.Api.Services.WithdrawalItemBuilder>();
builder.Services.AddSingleton<CrmMes.Api.Services.WorkOrderFactory>();
builder.Services.AddScoped<CrmMes.Api.Services.MaterialPricing>();
builder.Services.AddScoped<CrmMes.Api.Services.WorkOrderCosting>();
builder.Services.AddScoped<CrmMes.Api.Services.StockLedger>();
builder.Services.AddScoped<CrmMes.Api.Services.CustomFieldService>();
var sdiProvider = builder.Configuration["Sdi:Provider"]?.Trim().ToLowerInvariant();
if (sdiProvider == "http")
{
    builder.Services.AddHttpClient<CrmMes.Api.Services.ISdiProvider, CrmMes.Api.Services.HttpSdiProvider>(client =>
        client.Timeout = TimeSpan.FromSeconds(30));
}
else if (sdiProvider == "stub")
{
    builder.Services.AddSingleton<CrmMes.Api.Services.ISdiProvider, CrmMes.Api.Services.StubSdiProvider>();
}
else
{
    builder.Services.AddSingleton<CrmMes.Api.Services.ISdiProvider, CrmMes.Api.Services.UnconfiguredSdiProvider>();
}
builder.Services.AddHostedService<CrmMes.Api.Services.KeepWarmService>();
builder.Services.AddSingleton<CrmMes.Api.Services.LicenseState>();
builder.Services.AddSingleton<CrmMes.Api.Services.LicenseHeartbeatService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CrmMes.Api.Services.LicenseHeartbeatService>());
builder.Services.AddHttpClient(CrmMes.Api.Services.LicenseHeartbeatService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient(CrmMes.Api.Services.KeepWarmService.HttpClientName);

var aiProvider = builder.Configuration["Ai:Provider"]?.Trim().ToLowerInvariant() ?? "off";
if (aiProvider == "openai")
{
    builder.Services.AddHttpClient(CrmMes.Api.Services.HttpAiAssistant.HttpClientName,
        client => client.Timeout = TimeSpan.FromSeconds(60));
    builder.Services.AddSingleton<CrmMes.Api.Services.IAiAssistant, CrmMes.Api.Services.HttpAiAssistant>();
}
else if (aiProvider == "stub")
{
    builder.Services.AddSingleton<CrmMes.Api.Services.IAiAssistant, CrmMes.Api.Services.StubAiAssistant>();
}

var connectionString = Environment.GetEnvironmentVariable("NEON_DATABASE_URL")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Database non configurato. Imposta NEON_DATABASE_URL oppure ConnectionStrings:DefaultConnection.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // The migrations live in this assembly, not in CrmMes.Core where the DbContext is: without this
    // Migrate() at run time would find none.
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(Program).Assembly.GetName().Name));
});

var app = builder.Build();

// "CrmMes.Api --migrate": brings the database up to date and exits, used by the server installer and
// on every update. "Database:AutoMigrate": the same at every start (the Docker on-premise setup).
var migrateOnly = args.Contains("--migrate");
if (migrateOnly || app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Database aggiornato: {Count} migrazioni applicate", pending.Count);
    if (migrateOnly)
    {
        Console.WriteLine($"Database aggiornato: {pending.Count} migrazioni applicate.");
        return;
    }
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
// The technicians' web page (/tecnici, wwwroot/tecnici): same-origin only, no inline scripts, no
// framing — it handles a bearer token and a customer's signature.
app.Use(async (context, next) =>
{
    if (context.Request.Path.Value is "/tecnici" or "/app")
    {
        context.Response.Redirect(context.Request.Path.Value + "/");
        return;
    }

    // The web platform (/app, Blazor WebAssembly): same rules, plus 'wasm-unsafe-eval' which is what
    // lets the browser compile the .NET runtime (WebAssembly only, JavaScript eval stays forbidden), and
    // base-uri 'self' for its <base href>. Revalidated at every visit so an update shows up at once.
    if (context.Request.Path.StartsWithSegments("/app"))
    {
        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; " +
            "connect-src 'self'; font-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Cache-Control"] = "no-cache";
    }

    if (context.Request.Path.StartsWithSegments("/tecnici"))
    {
        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
            "base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Cache-Control"] = "no-cache";
    }

    // The JSON API (/api/*) and Swagger: no CSP (a browser never executes script from
    // application/json, so a content policy has nothing to restrict there), but the same
    // defense-in-depth headers every other surface already gets — a browser that ever renders an
    // error page or the Swagger UI from this origin shouldn't be framed, sniffed into a different
    // content type, or leak the referrer.
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/swagger"))
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    }

    await next();
});
app.UseDefaultFiles(); // /tecnici/ -> /tecnici/index.html
app.UseBlazorFrameworkFiles("/app"); // the web platform's runtime files (/app/_framework)
app.UseStaticFiles(); // serves wwwroot/favicon.ico, picked up automatically by the browser and by Swagger UI

app.UseSwagger();
app.UseSwaggerUI(options => options.RoutePrefix = "swagger");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseAuthentication();
// A role that must use two-factor, on an account that hasn't set it up yet: only login and the setup
// itself answer until it is active (the client shows the setup screen).
app.Use(async (context, next) =>
{
    if (context.User.HasClaim(CrmMes.Api.Services.TwoFactorRules.SetupClaim, "required")
        && !CrmMes.Api.Services.TwoFactorRules.IsAllowedDuringSetup(context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = "Per il tuo ruolo serve la verifica in due passaggi: attivala per continuare.", twoFactorSetupRequired = true });
        return;
    }

    await next();
});
// Subscription suspended by the vendor console: only a limited overall view answers (dashboard, lists);
// details and every change get 402 with the reason, which the clients show.
app.Use(async (context, next) =>
{
    var license = context.RequestServices.GetRequiredService<CrmMes.Api.Services.LicenseState>();
    if (license.Enabled && context.User.Identity?.IsAuthenticated == true
        && !CrmMes.Api.Services.LicenseState.IsAllowedWhenSuspended(context.Request))
    {
        var snapshot = await license.CurrentAsync(context.RequestAborted);
        if (snapshot.IsSuspended)
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsJsonAsync(new
            {
                message = snapshot.Message ?? "Abbonamento sospeso: è disponibile solo la consultazione generale. Contatta l'assistenza per riattivarlo.",
                licenseSuspended = true,
            });
            return;
        }
    }

    await next();
});
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

// Deep links of the web platform (/app/commesse/...) are pages of the app, not files: serve its index.
app.MapFallbackToFile("app/{*path:nonfile}", "app/index.html");

// Liveness without touching the database: what keeps the web service awake outside working hours,
// so the database (billed by compute time) can still sleep at night.
app.MapGet("/ping", () => Results.Ok(new { status = "ok" }));

app.MapGet("/health", async (ApplicationDbContext db) =>
{
    try
    {
        var isConnected = await db.Database.CanConnectAsync();
        return Results.Ok(new { status = isConnected ? "healthy" : "unavailable", database = "postgres" });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database health check failed");
        return Results.Problem(title: "Database connection failed", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Standard ASP.NET Core health-check shape, one entry per dependency (today just "postgres"; a future
// dependency — SDI, the AI provider — registers here too). For external monitoring tooling, not for the
// desktop client's own startup retry, which keeps using the plain /health above.
app.MapHealthChecks("/health/detailed", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds
            }),
            totalDurationMs = report.TotalDuration.TotalMilliseconds
        };
        await context.Response.WriteAsJsonAsync(payload);
    }
});

app.Run();

// Marker per WebApplicationFactory<Program> nei test di integrazione.
public partial class Program;
