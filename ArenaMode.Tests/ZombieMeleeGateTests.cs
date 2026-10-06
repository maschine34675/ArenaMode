using ArenaMode.Bots;

namespace ArenaMode.Tests;

public sealed class ZombieMeleeGateTests
{
    [Fact]
    public void DrivesWhenZombieRaidInfectedAndAlreadyEquipped()
    {
        Assert.True(ZombieMeleeGate.ShouldDrive(
            isZombieRaid: true, isInfected: true, meleeEquipped: true, canChangeToMelee: false));
    }

    [Fact]
    public void DrivesWhenNotYetEquippedButCanChangeToMelee()
    {
        // Mirrors BotMeleeWeaponData.RunToEnemyUpdate's own first branch: a
        // bot not currently holding its melee weapon, but whose Selector
        // CAN change to one, still gets the native body (which switches
        // weapons itself as its very first step).
        Assert.True(ZombieMeleeGate.ShouldDrive(
            isZombieRaid: true, isInfected: true, meleeEquipped: false, canChangeToMelee: true));
    }

    [Fact]
    public void NeverDrivesOutsideAZombieRaid()
    {
        // The bug this guards (zombie-melee-gate-outside-zombie-raid):
        // without the isZombieRaid term, an infectedAssault/Pmc/Civil/
        // Laborant bot that exists in ANY other raid (a genuine Halloween
        // event, or a debug spawn) would have its melee driven by this
        // patch too, breaking AGENTS.md's "normal raids must be
        // bit-identical" rule.
        Assert.False(ZombieMeleeGate.ShouldDrive(
            isZombieRaid: false, isInfected: true, meleeEquipped: true, canChangeToMelee: true));
    }

    [Fact]
    public void NeverDrivesANonInfectedBot()
    {
        // The bug this guards: without the isInfected term, this patch
        // would also take over the oneMeleeAttack node for OTHER bots
        // during a live Zombie raid (any ordinary melee-capable Scav/PMC),
        // which must keep running through SAIN/vanilla exactly as before
        // this fix.
        Assert.False(ZombieMeleeGate.ShouldDrive(
            isZombieRaid: true, isInfected: false, meleeEquipped: true, canChangeToMelee: true));
    }

    [Fact]
    public void NeverDrivesABotThatCannotMeleeAtAll()
    {
        // The bug this guards: without this term, the patch would call the
        // reverse-patched/fallback body on a bot with no melee weapon and
        // no way to switch to one. The native body's own first branch would
        // just set ShallEndRun and return false harmlessly, but driving it
        // at all every frame for a bot that structurally cannot melee is
        // needless and would mask the gate not actually discriminating.
        Assert.False(ZombieMeleeGate.ShouldDrive(
            isZombieRaid: true, isInfected: true, meleeEquipped: false, canChangeToMelee: false));
    }
}
