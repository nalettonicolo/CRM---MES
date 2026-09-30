using System.Net;
using System.Text.RegularExpressions;
using CrmMes.Console.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrmMes.Console.Tests;

/// <summary>The real console pipeline on an in-memory SQLite database.</summary>
public sealed class ConsoleFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ConsoleFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Console", "Host=localhost;Database=unused");
        builder.UseSetting("Console:Secret", "console-test-secret-console-test-secret-0123");
        builder.UseSetting("Database:AutoMigrate", "false");
        builder.UseSetting("Database:EnsureCreated", "true");
        builder.UseSetting("RateLimits:LoginPerMinute", "100000");
        builder.UseSetting("RateLimits:HeartbeatPerMinute", "100000");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ConsoleDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ConsoleDbContext>>();
            services.AddDbContext<ConsoleDbContext>(options => options.UseSqlite(_connection));
        });
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public ConsoleDbContext Db() => Services.CreateScope().ServiceProvider.GetRequiredService<ConsoleDbContext>();

    public override async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }
}

public static class FormPosting
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>Loads the page for its anti-forgery token (as a browser would), then posts the form.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string pageUrl, Dictionary<string, string> fields, string? postUrl = null)
    {
        var page = await client.GetStringAsync(pageUrl);
        var match = Token.Match(page);
        Assert.True(match.Success, "token antifalsificazione non trovato in " + pageUrl);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await client.PostAsync(postUrl ?? pageUrl, new FormUrlEncodedContent(fields));
    }
}
