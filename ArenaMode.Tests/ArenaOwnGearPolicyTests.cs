using ArenaMode.Modes;
using ArenaMode.Shared;

namespace ArenaMode.Tests;

/// <summary>
/// Pure tests for the own-gear table and its frozen-selection gate: the one
/// place ArenaSession.OwnGearArmed/Active turn the frozen bool plus a mode id
/// into "own gear is active for this raid", and the one place the left-panel
/// row asks whether to show itself. See ArenaOwnGearPolicy's own doc comment
/// and docs/reviews/OWN-GEAR-ALL-MODES-20261109.md.
/// </summary>
public sealed class ArenaOwnGearPolicyTests
{
    private static readonly string[] OwnGearModes =
    {
        DeathmatchSelection.ModeId, ArenaPopulationPolicy.TeamDeathmatch, ArenaPopulationPolicy.Domination,
        ArenaPopulationPolicy.Zombie,
    };

    [Fact]
    public void OffSelectionIsNeverActiveOnAnyMode()
    {
        foreach (var mode in OwnGearModes)
            Assert.False(ArenaOwnGearPolicy.IsSelectedFor(mode, ownGearSelection: false), mode);
        Assert.False(ArenaOwnGearPolicy.IsSelectedFor("ZOMBIE", ownGearSelection: false));
    }

    [Fact]
    public void OnSelectionIsActiveOnEveryModeButGunGameAndTactical()
    {
        foreach (var mode in OwnGearModes)
            Assert.True(ArenaOwnGearPolicy.IsSelectedFor(mode, ownGearSelection: true), mode);

        // Gun Game owns its loadout (the ladder), solo Tactical its buy economy
        // (user decision 2026-11-09); the legacy Deathmatch ids are normalized
        // away before arming and must not open a second door.
        foreach (var otherMode in new[] { "gungame", ArenaPopulationPolicy.Tactical, "endless", "rounds", "ctf", "unknown" })
        {
            Assert.False(ArenaOwnGearPolicy.IsSelectedFor(otherMode, ownGearSelection: true),
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
        Assert.True(ArenaOwnGearPolicy.IsSelectedFor("ZOMBIE", ownGearSelection: true));
        Assert.True(ArenaOwnGearPolicy.IsSelectedFor("Deathmatch", ownGearSelection: true));
        Assert.True(ArenaOwnGearPolicy.IsSelectedFor("  teamdeathmatch  ", ownGearSelection: true));
        Assert.True(ArenaOwnGearPolicy.IsOffered(" DOMINATION ", protectedTeam: false));
        Assert.True(ArenaOwnGearPolicy.IsOffered(" TACTICAL ", protectedTeam: true));
    }

    [Fact]
    public void AMissingModeIsNeverActiveOrOffered()
    {
        Assert.False(ArenaOwnGearPolicy.IsSelectedFor(null!, ownGearSelection: true));
        Assert.False(ArenaOwnGearPolicy.IsSelectedFor(string.Empty, ownGearSelection: true));
        Assert.False(ArenaOwnGearPolicy.IsSelectedFor("   ", ownGearSelection: true));
        Assert.False(ArenaOwnGearPolicy.IsOffered(null!, protectedTeam: false));
        Assert.False(ArenaOwnGearPolicy.IsOffered(string.Empty, protectedTeam: true));
    }

    [Fact]
    public void SoloOffersTheChoiceForEveryModeButGunGameAndTactical()
    {
        foreach (var mode in OwnGearModes)
            Assert.True(ArenaOwnGearPolicy.IsOffered(mode, protectedTeam: false), mode);
        Assert.False(ArenaOwnGearPolicy.IsOffered("gungame", protectedTeam: false));
        Assert.False(ArenaOwnGearPolicy.IsOffered(ArenaPopulationPolicy.Tactical, protectedTeam: false),
            "solo Tactical keeps its buy economy");
    }

    [Fact]
    public void AProtectedSquadOffersTheChoiceForTacticalOnly()
    {
        Assert.True(ArenaOwnGearPolicy.IsOffered(ArenaPopulationPolicy.Tactical, protectedTeam: true));
        foreach (var mode in new[]
                 {
                     DeathmatchSelection.ModeId, ArenaPopulationPolicy.TeamDeathmatch, ArenaPopulationPolicy.Domination,
                     ArenaPopulationPolicy.Zombie, "gungame",
                 })
        {
            Assert.False(ArenaOwnGearPolicy.IsOffered(mode, protectedTeam: true),
                $"a protected squad must not see the equipment choice on '{mode}'");
        }
    }

    [Fact]
    public void TheArmFreezesTheSoloPreferenceAloneAndByRosterForASquad()
    {
        foreach (var mode in OwnGearModes)
        {
            Assert.True(ArenaOwnGearPolicy.SelectionAtArm(mode, protectedTeam: false, soloPreference: true), mode);
            Assert.False(ArenaOwnGearPolicy.SelectionAtArm(mode, protectedTeam: false, soloPreference: false), mode);
        }

        // A protected squad never reads the per-mode preference: Zombies keeps
        // own gear without a choice, Tactical decides through its own
        // TeamOwnGear machinery (frozen false here), whatever the solo value.
        foreach (var preference in new[] { false, true })
        {
            Assert.True(ArenaOwnGearPolicy.SelectionAtArm(ArenaPopulationPolicy.Zombie, protectedTeam: true, preference));
            Assert.True(ArenaOwnGearPolicy.SelectionAtArm(" ZOMBIE ", protectedTeam: true, preference));
            Assert.False(ArenaOwnGearPolicy.SelectionAtArm(ArenaPopulationPolicy.Tactical, protectedTeam: true, preference));
            Assert.False(ArenaOwnGearPolicy.SelectionAtArm(DeathmatchSelection.ModeId, protectedTeam: true, preference));
            Assert.False(ArenaOwnGearPolicy.SelectionAtArm(null!, protectedTeam: true, preference));
        }
    }

    [Fact]
    public void TheTableAndTheGateNameTheSameSoloModes()
    {
        // The row shows itself exactly where a frozen "on" would activate: a
        // mode offered without a working gate (or the reverse) would promise
        // own gear in the menu and hand out a kit in the raid.
        foreach (var mode in new[] { "deathmatch", "teamdeathmatch", "domination", "tactical", "zombie", "gungame", "ctf" })
        {
            Assert.Equal(ArenaOwnGearPolicy.IsOffered(mode, protectedTeam: false),
                ArenaOwnGearPolicy.IsSelectedFor(mode, ownGearSelection: true));
        }
    }
}
