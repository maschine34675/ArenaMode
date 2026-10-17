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
        string plugins = Path.Combine(Path.GetTempPath(), "ArenaPathChecks", "BepInEx", "plugins");
        string expected = Path.Combine(plugins, "ArenaMode", relative);
        foreach (string dll in new[] { Path.Combine(plugins, "maschine-ArenaMode.Client.dll"),
                     Path.Combine(plugins, "ArenaMode", "maschine-ArenaMode.Client.dll") })
            Assert.Equal(expected, Path.Combine(ArenaAssetPaths.FromPluginFile(dll), relative));
    }

    [Fact]
    public void GroupedFolderAcceptsCaseAndTrailingSeparators()
    {
        string grouped = Path.Combine(Path.GetTempPath(), "plugins", "arenamode");
        Assert.Equal(grouped, ArenaAssetPaths.FromPluginDirectory(grouped + Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingPluginDirectoryCannotRedirectToWorkingDirectory(string? directory) =>
        Assert.Throws<ArgumentException>(() => ArenaAssetPaths.FromPluginDirectory(directory!));
}
