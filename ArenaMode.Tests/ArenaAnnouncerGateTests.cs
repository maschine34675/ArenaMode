using ArenaMode.Modes;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// Pure tests for item 2 of the 2026-10-06 follow-up ("announcer mutator"):
/// the per-mode default table and the master+per-mode decision
/// (<see cref="ArenaAnnouncerGate"/>). Mirrors the shape of
/// <c>ZombieOwnGearGateTests</c>/<c>ArenaEquipmentChoiceTests</c> (plain
/// static-method assertions, no EFT/Unity dependency).
/// </summary>
public sealed class ArenaAnnouncerGateTests
{
    [Fact]
    public void ZombieDefaultsOffEveryOtherModeDefaultsOn()
    {
        Assert.False(ArenaAnnouncerGate.DefaultEnabled(ArenaPopulationPolicy.Zombie));
        Assert.False(ArenaAnnouncerGate.DefaultEnabled("ZOMBIE"));
        Assert.False(ArenaAnnouncerGate.DefaultEnabled("  zombie  "));

        foreach (var otherMode in new[]
                 {
                     ArenaPopulationPolicy.Tactical, ArenaPopulationPolicy.TeamDeathmatch,
                     "deathmatch", "gungame", "endless", "rounds",
                 })
        {
            Assert.True(ArenaAnnouncerGate.DefaultEnabled(otherMode),
                $"the announcer must default on for mode '{otherMode}'");
        }
    }

    [Fact]
    public void AMissingModeDefaultsOnLikeEveryNonZombieMode()
    {
        // No mode this table does not recognise is Zombie, so the cautious
        // answer (same direction as every other mode) is "on" — unlike
        // ArenaPopulationPolicy's own unrecognised-mode default, which
        // deliberately goes the OTHER way for a different reason (see that
        // file's doc comment). A missing mode here is far more likely a
        // defensive null/empty guard than an actual unannounced new mode.
        Assert.True(ArenaAnnouncerGate.DefaultEnabled(null!));
        Assert.True(ArenaAnnouncerGate.DefaultEnabled(string.Empty));
        Assert.True(ArenaAnnouncerGate.DefaultEnabled("   "));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void BothTheMasterAndThePerModeSwitchMustAgree(bool masterEnabled, bool perModeEnabled, bool expected)
        => Assert.Equal(expected, ArenaAnnouncerGate.IsEnabled(masterEnabled, perModeEnabled));
}
