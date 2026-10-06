using ArenaMode.Modes;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// Pure tests for item 4 ("own protected gear")'s frozen-selection gate: the
/// one place ArenaSession.ZombieOwnGearArmed/Active turn the frozen F12 bool
/// plus a mode id into "own gear is active for this raid". See
/// ZombieOwnGearGate's own doc comment.
/// </summary>
public sealed class ZombieOwnGearGateTests
{
    [Fact]
    public void OffSelectionIsNeverActiveEvenOnTheZombieMode()
    {
        Assert.False(ZombieOwnGearGate.IsSelectedFor(ArenaPopulationPolicy.Zombie, ownGearSelection: false));
        Assert.False(ZombieOwnGearGate.IsSelectedFor("ZOMBIE", ownGearSelection: false));
    }

    [Fact]
    public void OnSelectionIsOnlyActiveOnTheZombieMode()
    {
        Assert.True(ZombieOwnGearGate.IsSelectedFor(ArenaPopulationPolicy.Zombie, ownGearSelection: true));

        foreach (var otherMode in new[]
                 {
                     ArenaPopulationPolicy.Tactical, ArenaPopulationPolicy.TeamDeathmatch,
                     "deathmatch", "gungame", "endless", "rounds",
                 })
        {
            Assert.False(ZombieOwnGearGate.IsSelectedFor(otherMode, ownGearSelection: true),
                $"own gear must not leak into mode '{otherMode}'");
        }
    }

    [Fact]
    public void TheComparisonIsCaseAndWhitespaceInsensitive()
    {
        // ArmedMode/ActiveMode are frozen from whatever ArenaModeCatalog
        // resolved at selection time, which already normalizes case — but a
        // mid-loading desync (or a future caller that skips normalization)
        // must not silently turn the player's own-gear choice off.
        Assert.True(ZombieOwnGearGate.IsSelectedFor("ZOMBIE", ownGearSelection: true));
        Assert.True(ZombieOwnGearGate.IsSelectedFor("Zombie", ownGearSelection: true));
        Assert.True(ZombieOwnGearGate.IsSelectedFor("  zombie  ", ownGearSelection: true));
    }

    [Fact]
    public void AMissingModeIsNeverActive()
    {
        Assert.False(ZombieOwnGearGate.IsSelectedFor(null!, ownGearSelection: true));
        Assert.False(ZombieOwnGearGate.IsSelectedFor(string.Empty, ownGearSelection: true));
        Assert.False(ZombieOwnGearGate.IsSelectedFor("   ", ownGearSelection: true));
    }
}
