using CrmMes.Core.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrmMes.Api.Tests;

/// <summary>
/// Boots the real API pipeline (auth, policies, controllers) against an isolated in-memory
/// Sqlite database instead of Neon, so integration tests never touch production data.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    /// <summary>Set before first use to test the production rate limits instead of the relaxed ones.</summary>
    public (int Auth, int Pin)? RateLimitOverride { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Program.cs requires a connection string to be present at startup even though
        // we replace the DbContext registration below; this satisfies that check.
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("Jwt:Key", "integration-test-signing-key-at-least-32-chars-long");
        // The suite logs in hundreds of times from one address; the real limits are exercised by
        // SecurityTests with their own factory.
        builder.UseSetting("RateLimits:AuthPerMinute", RateLimitOverride?.Auth.ToString() ?? "100000");
        builder.UseSetting("RateLimits:PinPerMinute", RateLimitOverride?.Pin.ToString() ?? "100000");

        builder.ConfigureServices(services =>
        {
            // AddDbContext registers more than DbContextOptions<T>: it also adds a per-provider
            // IDbContextOptionsConfiguration<T>. Leaving the Npgsql one behind makes EF Core see
            // two providers registered at once and throw, so both must be removed before re-adding Sqlite.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
