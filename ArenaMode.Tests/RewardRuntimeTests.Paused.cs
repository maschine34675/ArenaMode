using System.Security.Cryptography;
using ArenaMode.Server.Rewards;
using ArenaMode.Server.Routers;
using Xunit;

namespace ArenaMode.Tests;

public sealed partial class RewardRuntimeTests
{
    // Every file under the fixture root, by content.
    private static SortedDictionary<string, string> Everything(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            result[Path.GetRelativePath(root, path)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        return result;
    }

    [Fact]
    public async Task PausedRewardsWriteNothingWhateverTheMenuOrTheGameDoes()
    {
        using var f = new Fixture();
        f.Coordinator.ObserveRewardsPaused("offline-consent-conflict", recoveryPending: true);
        var before = Everything(f.Root);
        string consent = File.ReadAllText(f.PathOf("consent.json"));

        // The menu switches rewards off: refused, the choice stays (a change would break the retry's consent check).
        var off = f.Coordinator.SetConsent(Fixture.ProfileId, false);
        var offAsync = await f.Coordinator.SetConsentAsync(Fixture.ProfileId, false, CancellationToken.None);
        Assert.Equal("withheld", off.State);
        Assert.Equal("withheld", offAsync.State);
        // A stale expected revision is no reason to answer "changed" while paused either: the card says rewards are paused.
        var stale = await f.Coordinator.SetConsentAsync(Fixture.ProfileId, false, CancellationToken.None, expectedRevision: 999);
        Assert.Equal("withheld", stale.State);
        // No control capability is issued, no run is captured.
        Assert.Equal("reward-control-unavailable", f.Coordinator.PrepareControl(Fixture.ProfileId, Guid.NewGuid().ToString("N")).Reason);
        f.Coordinator.ObserveSnapshot(Fixture.ProfileId, f.Profile);
        Assert.False(f.Coordinator.HasCapture(Fixture.ProfileId));

        Assert.Equal(consent, File.ReadAllText(f.PathOf("consent.json")));
        Assert.Equal(before, Everything(f.Root));
        Assert.True(RewardEnrollment.WantsRewards(f.Root, Fixture.ProfileId));
    }

    [Fact]
    public void ThePausedStatusIsWithheldAndNeverAsksForARestart()
    {
        using var f = new Fixture(); // enrolled and enabled: without the pause this reads "ready"
        Assert.Equal("ready", f.Coordinator.GetStatus(Fixture.ProfileId).State);
        f.Coordinator.ObserveRewardsPaused("offline-activation-proof-missing", recoveryPending: true);

        var status = f.Coordinator.GetStatus(Fixture.ProfileId);

        Assert.Equal("withheld", status.State);
        Assert.Equal(RewardUnavailableText.SessionUnsettled, status.Detail);
        Assert.False(status.CanChange);
        Assert.False(status.Enabled);
        Assert.False(ArenaRewardReturnListener.NeedsStartup(status)); // a restart cannot help: the return path must not loop
        Assert.Equal("offline-activation-proof-missing", f.Coordinator.StartupUnavailable);
    }
}
