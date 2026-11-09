using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// Pure tests for the left-panel EQUIPMENT row: the per-mode availability table
/// (<see cref="ArenaEquipmentChoice"/>, delegating to ArenaOwnGearPolicy) and
/// the row state built on it (<see cref="ArenaEquipmentChoiceState"/>). Mirrors
/// the shape of <c>ArenaOwnGearPolicyTests</c> and the DM/TDM rules state
/// fixtures in <c>tools/TdmMatchMenuChecks</c>/<c>tools/DeathmatchMenuChecks</c>
/// (fake getter/setter/locked closures, no EFT/Unity dependency).
/// </summary>
public sealed class ArenaEquipmentChoiceTests
{
    private static readonly string[] SoloModes =
    {
        "deathmatch", "teamdeathmatch", "domination", "tactical", ArenaPopulationPolicy.Zombie,
    };

    [Fact]
    public void EveryModeButGunGameOffersAChoiceWithoutAProtectedTeam()
    {
        foreach (var mode in SoloModes)
            Assert.True(ArenaEquipmentChoice.IsOfferedFor(mode), mode);
        Assert.True(ArenaEquipmentChoice.IsOfferedFor("ZOMBIE"));
        Assert.True(ArenaEquipmentChoice.IsOfferedFor("  deathmatch  "));

        foreach (var otherMode in new[] { "gungame", "endless", "rounds" })
        {
            Assert.False(ArenaEquipmentChoice.IsOfferedFor(otherMode),
                $"equipment choice must not be offered for mode '{otherMode}'");
        }
    }

    [Fact]
    public void AMissingModeIsNeverOffered()
    {
        Assert.False(ArenaEquipmentChoice.IsOfferedFor(null!));
        Assert.False(ArenaEquipmentChoice.IsOfferedFor(string.Empty));
        Assert.False(ArenaEquipmentChoice.IsOfferedFor("   "));
    }

    [Fact]
    public void LabelNamesTheTwoOptions()
    {
        Assert.Equal("Arena kits", ArenaEquipmentChoice.Label(ownGear: false));
        Assert.Equal("Own gear", ArenaEquipmentChoice.Label(ownGear: true));
    }

    /// <summary>In-memory stand-in for Settings' per-mode-and-roster backing
    /// entries, same shape the tools harnesses use for DM/TDM rules fixtures.</summary>
    private sealed class Fixture
    {
        internal readonly Dictionary<(string Mode, bool ProtectedTeam), bool> Backing = new();
        internal int SetCalls;
        internal bool Locked;
        internal readonly ArenaEquipmentChoiceState State;

        internal Fixture()
        {
            State = new ArenaEquipmentChoiceState(
                (mode, team) => Backing.TryGetValue((mode, team), out var value) && value,
                (mode, team, value) => { SetCalls++; Backing[(mode, team)] = value; return true; },
                () => Locked);
        }
    }

    [Fact]
    public void ChoiceIsVisibleForEverySoloModeButGunGame()
    {
        var fx = new Fixture();

        fx.State.Synchronize("gungame", tacticalOnly: false);
        Assert.False(fx.State.Visible, "Gun Game must not show the equipment choice");
        Assert.False(fx.State.Editable);
        Assert.False(fx.State.TrySelect(true));
        Assert.Equal(0, fx.SetCalls);

        foreach (var mode in SoloModes)
        {
            fx.State.Synchronize(mode, tacticalOnly: false);
            Assert.True(fx.State.Visible, $"mode '{mode}' must show the equipment choice");
            Assert.True(fx.State.Editable);
        }
    }

    [Fact]
    public void EachModeKeepsItsOwnPreference()
    {
        var fx = new Fixture();
        fx.State.Synchronize("deathmatch", tacticalOnly: false);
        Assert.True(fx.State.TrySelect(true));
        Assert.True(fx.Backing[("deathmatch", false)]);

        foreach (var other in new[] { "teamdeathmatch", "domination", "tactical", ArenaPopulationPolicy.Zombie })
        {
            fx.State.Synchronize(other, tacticalOnly: false);
            Assert.False(fx.State.OwnGear, $"Deathmatch's choice must not leak into '{other}'");
        }

        fx.State.Synchronize("deathmatch", tacticalOnly: false);
        Assert.True(fx.State.OwnGear);
        Assert.Equal(1, fx.SetCalls);
    }

    [Fact]
    public void ProtectedZombiesKeepTheirOwnGearWithoutAChoice()
    {
        var fx = new Fixture();
        // PFT Zombies still preserve entry gear; only PFT Tactical offers kits.
        fx.State.Synchronize(ArenaPopulationPolicy.Zombie, tacticalOnly: true);
        Assert.False(fx.State.Visible, "protected Zombies must not show an equipment choice row");
        Assert.False(fx.State.TrySelect(true));
    }

    [Fact]
    public void ProtectedTacticalOffersItsOwnIndependentPreference()
    {
        var fx = new Fixture();
        fx.Backing[("tactical", true)] = true;
        fx.Backing[("tactical", false)] = true;
        fx.Backing[("zombie", false)] = true;
        fx.State.Synchronize("tactical", tacticalOnly: true);
        Assert.True(fx.State.Visible);
        Assert.True(fx.State.OwnGear);
        Assert.True(fx.State.TrySelect(false));
        Assert.False(fx.Backing[("tactical", true)]);
        Assert.True(fx.Backing[("tactical", false)], "the solo Tactical preference is a different entry");
        Assert.True(fx.Backing[("zombie", false)]);
        fx.Locked = true;
        Assert.False(fx.State.TrySelect(true));
        Assert.False(fx.State.OwnGear);
        fx.Locked = false;
        fx.State.Synchronize("tactical", tacticalOnly: false);
        Assert.True(fx.State.Visible, "solo Tactical offers the row too");
        Assert.True(fx.State.OwnGear, "solo Tactical reads its own entry");
        fx.State.Synchronize("tactical", tacticalOnly: true);
        Assert.False(fx.State.OwnGear);
    }

    [Theory]
    [InlineData("tactical", true)]
    [InlineData(" TACTICAL ", true)]
    [InlineData("zombie", false)]
    [InlineData("gungame", false)]
    [InlineData("teamdeathmatch", false)]
    [InlineData("domination", false)]
    [InlineData("deathmatch", false)]
    [InlineData(null, false)]
    public void ProtectedChoiceIsOnlyAvailableForTactical(string? mode, bool offered)
        => Assert.Equal(offered, ArenaEquipmentChoice.IsOfferedFor(mode!, protectedTeam: true));

    [Fact]
    public void TogglingWritesThroughTheInjectedSetter()
    {
        var fx = new Fixture();
        fx.State.Synchronize(ArenaPopulationPolicy.Zombie, tacticalOnly: false);

        Assert.False(fx.State.OwnGear);
        Assert.True(fx.State.TrySelect(true));
        Assert.True(fx.State.OwnGear);
        Assert.True(fx.Backing[(ArenaPopulationPolicy.Zombie, false)]);
        Assert.Equal(1, fx.SetCalls);

        Assert.True(fx.State.TrySelect(false));
        Assert.False(fx.State.OwnGear);
        Assert.False(fx.Backing[(ArenaPopulationPolicy.Zombie, false)]);
        Assert.Equal(2, fx.SetCalls);
    }

    [Fact]
    public void LockedDuringArmOrRaidRejectsWritesButStaysVisible()
    {
        var fx = new Fixture();
        fx.State.Synchronize("domination", tacticalOnly: false);
        fx.Locked = true;

        Assert.True(fx.State.Visible, "a frozen choice stays visible, same as DM/TDM rules while armed");
        Assert.False(fx.State.Editable);
        Assert.False(fx.State.TrySelect(true));
        Assert.Equal(0, fx.SetCalls);

        fx.Locked = false;
        Assert.True(fx.State.TrySelect(true));
        Assert.Equal(1, fx.SetCalls);
    }

    [Fact]
    public void ARejectedWriteCannotMoveTheDisplayedChoice()
    {
        var rejecting = new ArenaEquipmentChoiceState(
            (_, _) => false, (_, _, _) => false, () => false);
        rejecting.Synchronize("teamdeathmatch", tacticalOnly: false);
        Assert.False(rejecting.TrySelect(true));
        Assert.False(rejecting.OwnGear);
    }

    [Fact]
    public void LabelReflectsTheCurrentChoice()
    {
        var fx = new Fixture();
        fx.State.Synchronize("deathmatch", tacticalOnly: false);
        Assert.Equal("EQUIPMENT: Arena kits", fx.State.Label);
        fx.State.TrySelect(true);
        Assert.Equal("EQUIPMENT: Own gear", fx.State.Label);
    }
}
