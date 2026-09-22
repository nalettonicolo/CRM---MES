using System.IO;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

/// <summary>Every test uses its own temp file path (never the real %LOCALAPPDATA%\CrmMes\settings.json,
/// via the internal Load(path)/Save(path) test seam) so a test run can never clobber a real saved
/// server address.</summary>
public class ClientSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"crmmes-settings-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Load_FileDoesNotExist_ReturnsDefaultUrl()
    {
        var settings = ClientSettings.Load(_path);

        Assert.Equal(ClientSettings.DefaultApiBaseUrl, settings.ApiBaseUrl);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsApiBaseUrl()
    {
        var original = new ClientSettings { ApiBaseUrl = "https://crmmes-api.onrender.com/" };
        original.Save(_path);

        var loaded = ClientSettings.Load(_path);

        Assert.Equal("https://crmmes-api.onrender.com/", loaded.ApiBaseUrl);
    }

    [Fact]
    public void Load_CorruptedJson_FallsBackToDefaultInsteadOfThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not valid json ");

        var settings = ClientSettings.Load(_path);

        Assert.Equal(ClientSettings.DefaultApiBaseUrl, settings.ApiBaseUrl);
    }

    [Fact]
    public void Load_EmptyApiBaseUrlInFile_FallsBackToDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{\"ApiBaseUrl\":\"\"}");

        var settings = ClientSettings.Load(_path);

        Assert.Equal(ClientSettings.DefaultApiBaseUrl, settings.ApiBaseUrl);
    }

    [Theory]
    [InlineData("http://localhost:5092/", true)]
    [InlineData("http://127.0.0.1:5092/", true)]
    [InlineData("https://crmmes-api.onrender.com/", false)]
    [InlineData("not a valid url", true)]
    public void IsLocalHost_ReflectsWhetherAddressIsLoopback(string url, bool expected)
    {
        var settings = new ClientSettings { ApiBaseUrl = url };

        Assert.Equal(expected, settings.IsLocalHost());
    }
}
