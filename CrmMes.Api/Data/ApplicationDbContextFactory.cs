using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CrmMes.Api.Data;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var currentPath = Directory.GetCurrentDirectory();
        var apiPath = File.Exists(Path.Combine(currentPath, "appsettings.json"))
            ? currentPath
            : Path.Combine(currentPath, "CrmMes.Api");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(typeof(ApplicationDbContextFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = Environment.GetEnvironmentVariable("NEON_DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string Neon non configurata nei secret locali o nelle variabili d'ambiente.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString, options =>
            options.MigrationsAssembly(typeof(ApplicationDbContextFactory).Assembly));
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
