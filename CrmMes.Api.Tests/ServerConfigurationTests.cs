using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

[CollectionDefinition("ServerConfigurationFile", DisableParallelization = true)]
public class ServerConfigurationFileCollection;

/// <summary>A server at the customer's premises reads its settings from server.json (the installer writes it).
/// Runs alone: the file location is a process-wide environment variable.</summary>
[Collection("ServerConfigurationFile")]
public class ServerConfigurationTests
{
    [Fact]
    public async Task SettingsFile_IsLoaded_AndTheServerKnowsItIsOnPremise()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nicolomes-server-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
            {
              "Support": { "Name": "Assistenza Aurora", "Phone": "0123 456789", "RustDeskIdServer": "rd.example.test" }
            }
            """);
        Environment.SetEnvironmentVariable(ServerConfiguration.PathVariable, path);
        try
        {
            Assert.Equal(path, ServerConfiguration.ResolvePath());
            await using var factory = new CustomWebApplicationFactory();
            var info = await factory.CreateClient().GetFromJsonAsync<SupportInfoResponse>("/api/support/info");

            Assert.Equal("on-premise", info!.Hosting);
            Assert.Equal("Assistenza Aurora", info.Name);
            Assert.Equal("0123 456789", info.Phone);
            Assert.Equal("rd.example.test", info.RustDeskIdServer);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServerConfiguration.PathVariable, null);
            File.Delete(path);
        }
    }

    [Fact]
    public void WithoutOverride_TheFileLivesInProgramData() =>
        Assert.EndsWith(Path.Combine("NicoloMES", "server.json"), ServerConfiguration.DefaultPath());
}
