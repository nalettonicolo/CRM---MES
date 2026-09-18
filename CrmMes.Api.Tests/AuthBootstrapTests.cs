namespace CrmMes.Api.Tests;

/// <summary>
/// Tests the "first user ever registered becomes Admin" bootstrap rule. Each test needs a
/// database with a known, exact registration order, so it gets its own factory per test
/// method instead of sharing one via IClassFixture (xUnit does not guarantee method order,
/// and a shared fixture would make "who was first" flaky).
/// </summary>
public class AuthBootstrapTests : IAsyncLifetime
{
    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new CustomWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Register_FirstUser_BecomesAdmin()
    {
        var auth = await TestAuth.RegisterAsync(_client, "Prima Persona", "prima@test.local", "Password123!");

        Assert.Equal("Admin", auth.Role);
    }

    [Fact]
    public async Task Register_SecondUser_BecomesOperator()
    {
        await TestAuth.RegisterAsync(_client, "Uno", "uno-op@test.local", "Password123!");
        var second = await TestAuth.RegisterAsync(_client, "Due", "due-op@test.local", "Password123!");

        Assert.Equal("Operator", second.Role);
    }
}
