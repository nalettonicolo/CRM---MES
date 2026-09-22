using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestMethod
        | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPath
        | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseStatusCode
        | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.Duration;
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpLogging();
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
