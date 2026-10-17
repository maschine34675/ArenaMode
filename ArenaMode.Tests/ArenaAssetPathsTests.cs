using ArenaMode;
using Xunit;

namespace ArenaMode.Tests;

public class ArenaAssetPathsTests
{
    [Theory]
    [InlineData("sounds/double_kill.ogg")]
    [InlineData("hitmarkers/kill.wav")]
    [InlineData("pickups/collect.wav")]
    [InlineData("Pickups.json")]
    [InlineData("PickupDrafts")]
    [InlineData("AreaDrafts")]
    public void MovingPluginIntoAssetFolderPreservesAssetsAndUserData(string relative)
    {
        string plugins = Path.Combine(Path.GetTempPath(), "ArenaPathChecks-" + Guid.NewGuid().ToString("N"), "BepInEx", "plugins");
        string expected = Path.Combine(plugins, "maschine-ArenaMode", relative);
        foreach (string dll in new[] { Path.Combine(plugins, "maschine-ArenaMode.Client.dll"),
                     Path.Combine(plugins, "maschine-ArenaMode", "maschine-ArenaMode.Client.dll") })
            Assert.Equal(expected, Path.Combine(ArenaAssetPaths.FromPluginFile(dll), relative));
    }

    [Fact]
    public void GroupedFolderAcceptsCaseAndTrailingSeparators()
    {
        string grouped = Path.Combine(Path.GetTempPath(), "plugins", "arenamode");
        Assert.Equal(grouped, ArenaAssetPaths.FromPluginDirectory(grouped + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void CanonicalFolderAcceptsCaseAndTrailingSeparators()
    {
        string grouped = Path.Combine(Path.GetTempPath(), "plugins", "Maschine-ArenaMode");
        Assert.Equal(grouped, ArenaAssetPaths.FromPluginDirectory(grouped + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FlatLegacyInstallFallsBackUntilCanonicalAssetsExist()
    {
        string plugins = Path.Combine(Path.GetTempPath(), "ArenaPathChecks-" + Guid.NewGuid().ToString("N"));
        string legacy = Path.Combine(plugins, "ArenaMode");
        string canonical = Path.Combine(plugins, "maschine-ArenaMode");
        try
        {
            Directory.CreateDirectory(legacy);
            Assert.Equal(legacy, ArenaAssetPaths.FromPluginDirectory(plugins));
            Directory.CreateDirectory(canonical);
            Assert.Equal(canonical, ArenaAssetPaths.FromPluginDirectory(plugins));
            Assert.Equal(legacy, ArenaAssetPaths.FromPluginDirectory(legacy));
        }
        finally
        {
            // Only the three empty directories owned by this fixture, never a recursive delete.
            if (Directory.Exists(canonical)) Directory.Delete(canonical);
            if (Directory.Exists(legacy)) Directory.Delete(legacy);
            if (Directory.Exists(plugins)) Directory.Delete(plugins);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingPluginDirectoryCannotRedirectToWorkingDirectory(string? directory) =>
        Assert.Throws<ArgumentException>(() => ArenaAssetPaths.FromPluginDirectory(directory!));
}
