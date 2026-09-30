namespace CrmMes.Api.Services;

/// <summary>Where a server installed at the customer's premises keeps its settings (database, signing key,
/// listening address, log folder, support contacts): one JSON file outside the program folder, written by
/// server/Install-NicoloMESServer.ps1 and readable only by the service account and administrators, so
/// reinstalling or updating the program never touches it. The cloud deploy (Render) has no such file and
/// keeps using environment variables.</summary>
public static class ServerConfiguration
{
    /// <summary>Overrides the default location (tests, a second instance on the same machine).</summary>
    public const string PathVariable = "CRM_MES_CONFIG";

    public static string ResolvePath()
    {
        var overridden = Environment.GetEnvironmentVariable(PathVariable);
        return !string.IsNullOrWhiteSpace(overridden)
            ? overridden
            : DefaultPath();
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NicoloMES", "server.json");

    /// <summary>"on-premise" when started with the customer's settings file, "cloud" on Render, "altro"
    /// otherwise (development, a container started by hand).</summary>
    public static string HostingMode(bool configFileLoaded) =>
        configFileLoaded ? "on-premise"
        : !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER")) ? "cloud"
        : "altro";
}
