using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// Pure tests for item 1 of the 2026-10-06 follow-up ("own gear to the
/// left"): the per-mode availability table (<see cref="ArenaEquipmentChoice"/>)
/// and the left-panel row state built on it (<see cref="ArenaEquipmentChoiceState"/>).
/// Mirrors the shape of <c>ZombieOwnGearGateTests</c> and the DM/TDM rules
/// state fixtures in <c>tools/TdmMatchMenuChecks</c>/<c>tools/
/// DeathmatchMenuChecks</c> (fake getter/setter/locked closures, no
/// EFT/Unity dependency).
/// </summary>
public sealed class ArenaEquipmentChoiceTests
{
    [Fact]
    public void OnlyZombieOffersAChoiceToday()
    {
        Assert.True(ArenaEquipmentChoice.IsOfferedFor(ArenaPopulationPolicy.Zombie));
        Assert.True(ArenaEquipmentChoice.IsOfferedFor("ZOMBIE"));
        Assert.True(ArenaEquipmentChoice.IsOfferedFor("  zombie  "));

        foreach (var otherMode in new[]
                 {
                     ArenaPopulationPolicy.Tactical, ArenaPopulationPolicy.TeamDeathmatch,
                     "deathmatch", "gungame", "endless", "rounds",
                 })
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

    /// <summary>In-memory stand-in for Settings' per-mode backing entry, same
    /// shape the tools harnesses use for DM/TDM rules state fixtures.</summary>
    private sealed class Fixture
    {
        internal readonly Dictionary<string, bool> Backing = new();
        internal int SetCalls;
        internal bool Locked;
        internal readonly ArenaEquipmentChoiceState State;

        internal Fixture()
        {
            State = new ArenaEquipmentChoiceState(
                mode => Backing.TryGetValue(mode, out var value) && value,
                (mode, value) => { SetCalls++; Backing[mode] = value; return true; },
                () => Locked);
        }
    }

    [Fact]
    public void ChoiceIsOnlyVisibleForZombie()
    {
        var fx = new Fixture();

        foreach (var mode in new[] { "deathmatch", "gungame", "tactical", "teamdeathmatch" })
        {
            fx.State.Synchronize(mode, tacticalOnly: false);
            Assert.False(fx.State.Visible, $"mode '{mode}' must not show the equipment choice");
            Assert.False(fx.State.Editable);
            Assert.False(fx.State.TrySelect(true));
            Assert.Equal(0, fx.SetCalls);
        }

        fx.State.Synchronize(ArenaPopulationPolicy.Zombie, tacticalOnly: false);
        Assert.True(fx.State.Visible);
        Assert.True(fx.State.Editable);
    }

    [Fact]
    public void ProtectedTeamNeverShowsTheRowEvenIfATableEntryExistedForIt()
    {
        var fx = new Fixture();
        // Zombie can never be a tacticalOnly mode in practice, but the guard
        // is defensive: assert it independently of ArenaEquipmentChoice's own
        // table so a future table entry cannot silently leak a toggle into a
        // protected-team screen.
        fx.State.Synchronize(ArenaPopulationPolicy.Zombie, tacticalOnly: true);
        Assert.False(fx.State.Visible, "a protected team must never show an equipment choice row");
        Assert.False(fx.State.TrySelect(true));
    }

    [Fact]
    public void TogglingWritesThroughTheInjectedSetter()
    {
        var fx = new Fixture();
        fx.State.Synchronize(ArenaPopulationPolicy.Zombie, tacticalOnly: false);

        Assert.False(fx.State.OwnGear);
        Assert.True(fx.State.TrySelect(true));
        Assert.True(fx.State.OwnGear);
        Assert.True(fx.Backing[ArenaPopulationPolicy.Zombie]);
        Assert.Equal(1, fx.SetCalls);

        Assert.True(fx.State.TrySelect(false));
        Assert.False(fx.State.OwnGear);
        Assert.False(fx.Backing[ArenaPopulationPolicy.Zombie]);
        Assert.Equal(2, fx.SetCalls);
    }

    [Fact]
    public void LockedDuringArmOrRaidRejectsWritesButStaysVisible()
    {
        var fx = new Fixture();
        fx.State.Synchronize(ArenaPopulationPolicy.Zombie, tacticalOnly: false);
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
            _ => false, (_, _) => false, () => false);
        rejecting.Synchronize(ArenaPopulationPolicy.Zombie, tacticalOnly: false);
        Assert.False(rejecting.TrySelect(true));
        Assert.False(rejecting.OwnGear);
    }

    [Fact]
    public void LabelReflectsTheCurrentChoice()
    {
        var fx = new Fixture();
        fx.State.Synchronize(ArenaPopulationPolicy.Zombie, tacticalOnly: false);
        Assert.Equal("EQUIPMENT: Arena kits", fx.State.Label);
        fx.State.TrySelect(true);
        Assert.Equal("EQUIPMENT: Own gear", fx.State.Label);
    }
}
