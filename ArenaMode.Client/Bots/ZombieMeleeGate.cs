namespace ArenaMode.Bots;

/// <summary>
/// Pure decision for whether <c>ZombieMeleePatch</c>'s prefix should take
/// over <c>OneMeleeAttackNode.UpdateNodeByBrain</c> itself — driving the
/// native <c>BotMeleeWeaponData.RunToEnemyUpdate</c> body directly — instead
/// of letting the vanilla node run (which, for every infected bot, means
/// letting SAIN's own global <c>RunToEnemyUpdate</c> prefix suppress it; see
/// <c>ZombieMeleePatch</c>'s own doc comment for the full chain). No
/// EFT/Unity types, so <c>ArenaMode.Tests</c> can link this file directly
/// (see <c>ArenaMode.Tests.csproj</c>'s <c>ClientRules</c> Compile Include
/// links) — the same pattern <c>ZombieWavePolicy</c>/<c>ZombieRoleMix</c>/
/// <c>ZombieProfilePlan</c> already use.
///
/// Every precondition that needs an EFT type to evaluate (the node's owner
/// non-null and alive, <c>WeaponManager</c>/<c>Melee</c> non-null) stays
/// inline in <c>ZombieMeleePatch.Prefix</c>, which calls this only once all
/// of those already hold — this class only decides the policy question once
/// the raw facts are in hand.
/// </summary>
internal static class ZombieMeleeGate
{
    /// <summary>
    /// True exactly when the patch should drive the node itself: the exact
    /// active raid is Zombie, the node's owner is an infected role, and the
    /// bot can actually swing a melee weapon right now — either it already
    /// has one equipped, or its own <c>WeaponSelector</c> reports it CAN
    /// change to one. This mirrors the two-branch check
    /// <c>BotMeleeWeaponData.RunToEnemyUpdate</c> itself opens with
    /// (../_decompiled/BotMeleeWeaponData.cs:135-143): <c>if (!IsMelee) { if
    /// (!CanChangeToMeleeWeapons) { ShallEndRun = true; return false; }
    /// ChangeToMelee(); } }</c> — a bot that cannot melee at all gets
    /// nothing from driving the native body, so the gate fails closed
    /// (returns false, vanilla/SAIN runs) for it rather than calling the
    /// reverse-patched or fallback body only to hit that same early return.
    ///
    /// A Shooting-difficulty infected (<c>InfectedLayersStrategy</c>'s
    /// <c>hard</c> =&gt; <c>Shooting</c>, per ZOMBIE-MODE-REVIEW.md) never
    /// reaches <c>oneMeleeAttack</c> in the first place — the attack layer
    /// picks a different node for it — so this gate does not special-case
    /// that brain mode: it only needs to recognise a bot that genuinely
    /// cannot melee, which the <c>meleeEquipped || canChangeToMelee</c> term
    /// already covers for every difficulty.
    /// </summary>
    internal static bool ShouldDrive(bool isZombieRaid, bool isInfected, bool meleeEquipped, bool canChangeToMelee)
        => isZombieRaid && isInfected && (meleeEquipped || canChangeToMelee);
}
