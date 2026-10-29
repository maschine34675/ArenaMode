using ArenaMode.SessionHelper;
using Xunit;

namespace ArenaMode.Tests;

/// <summary>
/// The helper refuses to run Arena through a linked install path. The refusal stays; its text used to be
/// the framework default ("Found invalid data while decoding.") and named nothing.
/// </summary>
public sealed class HelperLinkedPathMessageTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ArenaMode.LinkMessage.Tests", Guid.NewGuid().ToString("N"));

    public HelperLinkedPathMessageTests()
    {
        Directory.CreateDirectory(Path.Combine(_home, "real", "SPT"));
        File.WriteAllText(Path.Combine(_home, ".owner"), "synthetic-link-message");
    }

    [Fact]
    public void ALinkAnywhereOnThePathIsRefusedAndNamed()
    {
        string link = Path.Combine(_home, "alias");
        Directory.CreateSymbolicLink(link, Path.Combine(_home, "real"));
        string through = Path.Combine(link, "SPT");

        var error = Assert.Throws<InvalidDataException>(() => HelperRewardControlTransfer.RequirePlainPath(through, true));

        Assert.Contains(link, error.Message);
        Assert.Contains("link", error.Message);
        Assert.DoesNotContain("Found invalid data while decoding", error.Message);
        HelperRewardControlTransfer.RequirePlainPath(Path.Combine(_home, "real", "SPT"), true);
    }

    [Fact]
    public void AnUnnormalisedPathNamesItselfToo()
    {
        var error = Assert.Throws<InvalidDataException>(() => HelperRewardControlTransfer.RequirePlainPath("relative\\path", false));
        Assert.Contains("relative", error.Message);
        Assert.DoesNotContain("Found invalid data while decoding", error.Message);
    }

    public void Dispose()
    {
        string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ArenaMode.LinkMessage.Tests"));
        if (Path.GetDirectoryName(Path.GetFullPath(_home)) != expected
            || File.ReadAllText(Path.Combine(_home, ".owner")) != "synthetic-link-message") throw new InvalidOperationException("Unowned fixture.");
        string link = Path.Combine(_home, "alias");
        if (Directory.Exists(link)) Directory.Delete(link);
        Directory.Delete(_home, true);
    }
}
