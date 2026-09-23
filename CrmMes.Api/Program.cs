using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

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
});
builder.Services.AddSingleton<IPasswordHasher<CrmMes.Core.Models.User>, PasswordHasher<CrmMes.Core.Models.User>>();
builder.Services.AddScoped<CrmMes.Api.Services.WithdrawalItemBuilder>();

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
    options.UseNpgsql(connectionString);
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStaticFiles(); // serves wwwroot/favicon.ico, picked up automatically by the browser and by Swagger UI

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

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

app.Run();

// Marker per WebApplicationFactory<Program> nei test di integrazione.
public partial class Program;
