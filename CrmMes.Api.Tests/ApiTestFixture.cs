using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

/// <summary>One isolated API instance + Sqlite database per test class that uses it.</summary>
public class ApiTestFixture : IAsyncLifetime
{
    public CustomWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        Factory = new CustomWebApplicationFactory();
        await Factory.InitializeDatabaseAsync();
        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>
/// Same isolated environment as <see cref="ApiTestFixture"/>, but the very first user is
/// registered up front so it becomes Admin (bootstrap rule) and can provision other roles.
/// </summary>
public sealed class AdminSeededApiTestFixture : ApiTestFixture
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "AdminPass123!";

    public AuthResponse Admin { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Admin = await TestAuth.RegisterAsync(Client, "Test Admin", AdminEmail, AdminPassword);
    }
}
