using System.IO;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.0", "1.1.0.0", true)]
    [InlineData("v1.2.0", "1.2.0.0", false)]   // same version: the 4th assembly component must not matter
    [InlineData("v1.1.0", "1.2.0.0", false)]
    [InlineData("v1.10.0", "1.9.0.0", true)]   // numeric, not alphabetic
    [InlineData("1.3", "1.2.5.0", true)]
    [InlineData("release-finale", "1.0.0.0", false)]
    public void IsNewer_ComparesTagWithRunningVersion(string tag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(tag, Version.Parse(current), out _));
    }

    [Fact]
    public void AssetNames_MatchWhatTheReleasePipelinePublishes()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains(UpdateService.SetupAssetName, workflow);
        Assert.Contains(UpdateService.ChecksumAssetName, workflow);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CrmMes.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Radice del repository non trovata.");
    }
}
