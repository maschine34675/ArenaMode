using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Bots;

/// <summary>
/// Makes a Zombie-raid infected actually land its native melee attack.
///
/// FACTS (first GPU raid, SPT-GPU-2, 2026-10-06 12:52-13:20, build
/// 1e76582; full write-up in ZOMBIE-MODE-REVIEW.md, "Native melee is
/// suppressed by SAIN's global prefix"): everything up to the swing itself
/// worked — spawns, zombie bodies, knives in hand, the chase, the HUD — but
/// no zombie ever damaged the player in 25 minutes. Live inspection traced
/// this to a single root cause, independent of anything this mode's own
/// code does wrong:
///
/// SAIN (installed; its own StrictExclusionList, fetched from the server,
/// names `infectedAssault`/`infectedCivil`/`infectedLaborant`/`infectedPmc`/
/// `infectedTagilla` — SAINServerMod/Generators/DefaultBotTypes.cs:102-109 —
/// so SAIN never builds a `BotComponent` for any of them) patches
/// `BotMeleeWeaponData.RunToEnemyUpdate` with a PREFIX
/// (SAIN-master/SAIN/Patches/FixPatches.cs, `RunToEnemyUpdatePatch`):
/// <c>if (SAINEnableClass.GetSAIN(profileId, out bot) &amp;&amp;
/// bot.SAINLayersActive) { ...SAIN's own version...; return false; } return
/// false;</c> — note the UNCONDITIONAL <c>return false;</c> at the end,
/// reached whenever <c>GetSAIN</c> is false (which it always is for an
/// excluded role, since such a bot is never added to SAIN's own
/// <c>_enabledBots</c> set — SAIN/Plugin/SAINEnableClass.cs:23-68,257-272).
/// That one trailing `return false` skips Harmony's original-method call for
/// EVERY bot SAIN does not own — not just the infected ones, but it is the
/// one that bites here — so the native body
/// (../_decompiled/BotMeleeWeaponData.cs:132-208) never runs for a zombie,
/// and neither does SAIN's own replacement (that branch is gated on
/// `SAINLayersActive`, which an excluded bot never has). The chain that
/// should drive a swing — `OneMeleeAttackNode.UpdateNodeByBrain`
/// (../_decompiled/OneMeleeAttackNode.cs:10-16) calling
/// `WeaponManager.Melee.RunToEnemyUpdate()`, which every
/// `TRY_HIT_PERIOD_FALSE` (0.1s) calls `TryHit` → `KnifeController.
/// MakeKnifeKick()` — simply never starts.
///
/// THE FIX below does not touch SAIN (never patch a competing mod; see
/// AGENTS.md licence rules) and does not out-race SAIN's own prefix by
/// patch ORDERING (fragile across Harmony/BepInEx load order — explicitly
/// rejected in the fix design). Instead: a prefix on
/// `OneMeleeAttackNode.UpdateNodeByBrain` itself — one call site OUTSIDE
/// `BotMeleeWeaponData`, so SAIN's patch on the melee method is never even
/// reached for a Zombie-raid infected — reproduces that node's own two-line
/// body (`if (DoorOpen() == CanRun) Melee.RunToEnemyUpdate();`) but calls the
/// REAL native `RunToEnemyUpdate` body through a HarmonyX reverse patch
/// (`Harmony.CreateReversePatcher`, confirmed present in the installed
/// 2.9.0.0 `0Harmony.dll` — see `TryEnsureReversePatch` below), which copies
/// the original method's IL into a brand-new, patch-chain-free `MethodInfo`
/// — SAIN's prefix is simply never consulted, because nothing calls through
/// the ORIGINAL patched method at all. If the reverse patch cannot be
/// prepared, <see cref="FallbackRunToEnemyUpdate"/> is a hand-written,
/// line-cited copy of the same decompiled body instead; either way the path
/// actually in use is logged once (<see cref="LogPathChoiceOnce"/>), and if
/// even the fallback is somehow unreachable the prefix stays inert (returns
/// true — vanilla/SAIN-suppressed behaviour, exactly today's state) rather
/// than leaving the node half-driven.
///
/// Registered next to <see cref="ZombieNativeEventBridge"/> in Plugin.Awake,
/// same `TryEnable`-and-fail-closed convention. The Harmony instance that
/// performs the reverse patch is a SEPARATE, raw `Harmony` object (the
/// `PoolPrewarmMonitor` recipe in this repo — see its own class doc comment
/// — for a feature that needs direct Harmony API `SPT.Reflection.Patching.
/// ModulePatch` does not expose); the `OneMeleeAttackNode` prefix itself
/// still goes through `ModulePatch`, the same as every other hook in
/// `ZombieNativeEventBridge`.
/// </summary>
internal static class ZombieMeleePatch
{
    private static readonly Harmony ReversePatchHarmony = new(Plugin.PluginGuid + ".ZombieMelee");

    private static Func<BotMeleeWeaponData, bool> _reversePatchedOriginal;
    private static bool _reversePatchAttempted;
    private static bool _reversePatchDead;
    private static bool _loggedPathChoice;

    internal static void EnablePatch()
    {
        try
        {
            new MeleeAttackNodePatch().Enable();
            Plugin.ModLogger.LogInfo("Arena zombie melee: OneMeleeAttackNode prefix installed.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena zombie melee: the OneMeleeAttackNode prefix is unavailable ({ex.Message}) — infected "
                + "keep whatever melee behaviour SAIN's global RunToEnemyUpdate prefix leaves them (suppressed "
                + "for every bot SAIN does not own; see this class's own doc comment).");
        }
    }

    private sealed class MeleeAttackNodePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(OneMeleeAttackNode), nameof(OneMeleeAttackNode.UpdateNodeByBrain));

        /// <summary>
        /// Mirrors the native body (OneMeleeAttackNode.cs:10-16) exactly when
        /// ZombieMeleeGate says to drive it; returns true (vanilla/SAIN
        /// runs, unchanged) otherwise — including every non-Zombie raid and
        /// every non-infected bot, so this can never make a normal raid
        /// anything but bit-identical (AGENTS.md).
        /// </summary>
        [PatchPrefix]
        private static bool Prefix(OneMeleeAttackNode __instance)
        {
            try
            {
                var owner = __instance?._owner;
                if (owner == null || owner.HealthController == null || !owner.HealthController.IsAlive)
                    return true;

                var melee = owner.WeaponManager?.Melee;
                if (melee == null)
                    return true;

                var role = owner.Profile?.Info?.Settings?.Role;
                bool isInfected = role.HasValue && role.Value.IsInfected();
                bool meleeEquipped = melee.MeleeWeaponEquipped;
                bool canChangeToMelee = owner.WeaponManager.Selector != null
                    && owner.WeaponManager.Selector.CanChangeToMeleeWeapons;

                if (!ZombieMeleeGate.ShouldDrive(
                        ZombieNativeEventBridge.IsActiveZombieRaid(), isInfected, meleeEquipped, canChangeToMelee))
                    return true;

                if (__instance.DoorOpen() == DoorInteractionStatus.CanRun)
                    RunOriginal(melee);

                return false;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    $"Arena zombie melee: OneMeleeAttackNode prefix failed ({ex.Message}) — this call falls "
                    + "back to vanilla/SAIN-suppressed behaviour.");
                return true;
            }
        }
    }

    /// <summary>
    /// Runs the TRUE native <c>BotMeleeWeaponData.RunToEnemyUpdate</c> body —
    /// through the reverse patch when one could be prepared, the hand-written
    /// fallback otherwise. Never throws: a failure inside either path is
    /// caught and demotes to the other/no-op rather than ever propagating
    /// into the Harmony prefix above.
    /// </summary>
    private static void RunOriginal(BotMeleeWeaponData melee)
    {
        if (TryEnsureReversePatch())
        {
            try
            {
                _reversePatchedOriginal(melee);
                return;
            }
            catch (Exception ex)
            {
                // A throwing reverse-patched body is treated as permanently
                // broken for the rest of this raid (and process — nothing
                // about the native method changes mid-process) rather than
                // retried every call: fall back for good, once, and say so.
                _reversePatchDead = true;
                Plugin.ModLogger.LogWarning(
                    $"Arena zombie melee: the reverse-patched RunToEnemyUpdate threw ({ex.Message}) — switching "
                    + "to the hand-written fallback for the rest of this raid.");
                LogPathChoiceOnce(usingReversePatch: false);
            }
        }

        try
        {
            FallbackRunToEnemyUpdate(melee);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena zombie melee: the fallback RunToEnemyUpdate threw ({ex.Message}).");
        }
    }

    /// <summary>
    /// Lazily builds the reverse-patched delegate once per process. Returns
    /// false (caller uses the fallback) without retrying every call once
    /// preparation has failed or a prepared delegate has thrown — repeating
    /// a failing reflection/IL operation every melee tick would turn one
    /// startup problem into a per-frame cost.
    /// </summary>
    private static bool TryEnsureReversePatch()
    {
        if (_reversePatchDead)
            return false;
        if (_reversePatchedOriginal != null)
            return true;
        if (_reversePatchAttempted)
            return false;

        _reversePatchAttempted = true;
        try
        {
            var original = AccessTools.Method(typeof(BotMeleeWeaponData), nameof(BotMeleeWeaponData.RunToEnemyUpdate));
            if (original == null)
                throw new MissingMethodException("BotMeleeWeaponData.RunToEnemyUpdate");

            var standin = typeof(ZombieMeleePatch).GetMethod(nameof(ReversePatchStub), BindingFlags.NonPublic | BindingFlags.Static);
            var patched = ReversePatchHarmony.CreateReversePatcher(original, new HarmonyMethod(standin))
                .Patch(HarmonyReversePatchType.Original);
            _reversePatchedOriginal = (Func<BotMeleeWeaponData, bool>)patched.CreateDelegate(typeof(Func<BotMeleeWeaponData, bool>));
            LogPathChoiceOnce(usingReversePatch: true);
            return true;
        }
        catch (Exception ex)
        {
            _reversePatchDead = true;
            Plugin.ModLogger.LogWarning(
                $"Arena zombie melee: HarmonyX reverse patch unavailable ({ex.Message}) — using the hand-written "
                + "fallback copy of BotMeleeWeaponData.RunToEnemyUpdate instead (decompile lines 132-208).");
            LogPathChoiceOnce(usingReversePatch: false);
            return false;
        }
    }

    private static void LogPathChoiceOnce(bool usingReversePatch)
    {
        if (_loggedPathChoice)
            return;
        _loggedPathChoice = true;
        Plugin.ModLogger.LogInfo(usingReversePatch
            ? "Arena zombie melee: driving infected RunToEnemyUpdate through a HarmonyX reverse patch of the "
              + "native body (bypasses SAIN's global RunToEnemyUpdate prefix entirely)."
            : "Arena zombie melee: driving infected RunToEnemyUpdate through the hand-written fallback "
              + "reimplementation (decompile BotMeleeWeaponData.cs:132-208) — the HarmonyX reverse patch could "
              + "not be prepared.");
    }

    /// <summary>
    /// Shape-only stand-in for the reverse patch: HarmonyX replaces this
    /// method's IL with a copy of the ORIGINAL <c>RunToEnemyUpdate</c>'s IL
    /// (unaffected by SAIN's or anyone else's patches on that original) and
    /// hands back a new <see cref="MethodInfo"/> for the result — this
    /// method's own body below never actually runs. The first parameter
    /// stands in for the instance <c>RunToEnemyUpdate</c> is called on,
    /// exactly as HarmonyX's reverse-patch convention requires for an
    /// instance method's stand-in.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ReversePatchStub(BotMeleeWeaponData instance)
        => throw new NotSupportedException("Replaced by a HarmonyX reverse patch at runtime; never called directly.");

    /// <summary>
    /// Faithful reimplementation of the decompiled
    /// <c>BotMeleeWeaponData.RunToEnemyUpdate</c>
    /// (../_decompiled/BotMeleeWeaponData.cs:132-208), used only when the
    /// HarmonyX reverse patch above could not be prepared. Every member it
    /// touches — <c>_owner</c>, <c>WeaponManager</c>/<c>Selector</c>/
    /// <c>Melee</c>, <c>BotLay</c>, <c>Memory.GoalEnemy</c>, <c>Steering</c>,
    /// <c>Mover</c>, <c>Settings.FileSettings.Shoot</c>, and
    /// <c>BotMeleeWeaponData</c>'s own tuning fields — is public on the
    /// decompile (verified file by file; see ZombieNativeEventBridge's own
    /// doc comment for the precedent of relying on this "loose field" BSG
    /// style instead of reflection), so this needs no `AccessTools.Field`.
    ///
    /// One deliberate, named deviation from the native body: a null
    /// <c>GoalEnemy</c> returns false immediately instead of dereferencing
    /// it unchecked. The native body never null-checks it either, because
    /// <c>OneMeleeAttackNode</c> only reaches this call while the attack
    /// layer has already resolved a goal enemy — but this fallback only
    /// exists for when something ELSE about this raid's setup is already
    /// off (the reverse patch failing to prepare), so failing closed here
    /// instead of risking an NRE matches this codebase's fail-closed
    /// convention (AGENTS.md) rather than strict byte-for-byte fidelity.
    /// </summary>
    private static bool FallbackRunToEnemyUpdate(BotMeleeWeaponData melee)
    {
        var owner = melee._owner;
        melee.ShallEndRun = false;

        if (!owner.WeaponManager.IsMelee)
        {
            if (!owner.WeaponManager.Selector.CanChangeToMeleeWeapons)
            {
                melee.ShallEndRun = true;
                return false;
            }

            owner.WeaponManager.Selector.ChangeToMelee();
        }

        if (owner.BotLay.IsLay)
            owner.BotLay.GetUp(withCheck: false);

        owner.SetPose(1f);

        var goalEnemy = owner.Memory.GoalEnemy;
        if (goalEnemy == null)
            return false; // See the deviation note above.

        bool closeEnough = goalEnemy.Distance < melee.DIST_TO_HIT;
        if (closeEnough)
        {
            owner.Steering.LookToPoint(goalEnemy.GetBodyPartPosition());
            if (goalEnemy.Person.AIData.Player.MovementContext.IsInPronePose)
                owner.SetPose(0f);
        }
        else
        {
            owner.SetPose(1f);
            owner.Steering.LookToMovingDirection();
        }

        owner.Sprint(melee.Running && goalEnemy.Distance > melee.DIST_TO_STOP_SPRINT);

        if (melee._nextTryHitTime < Time.time)
        {
            bool hit = closeEnough && melee.TryHit(goalEnemy);
            melee.ResetHitTime(hit ? 10f : melee.TRY_HIT_PERIOD_FALSE);
        }

        if (owner.Mover.HasPathAndNoComplete)
        {
            if (melee._runPathCheck < Time.time)
            {
                float recalc = !melee._useZigZag
                    ? (goalEnemy.Distance > melee.FAR_DIST ? melee.farRecalc
                        : (goalEnemy.Distance > melee.MID_DIST ? melee.midRecalc : melee.closeRecalc))
                    : (goalEnemy.Distance > melee.FAR_DIST ? melee.farRecalc
                        : (goalEnemy.Distance > melee.MID_DIST ? melee.midRecalcZZ : melee.closeRecalcZZ));
                melee._runPathCheck = Time.time + recalc;

                if (!melee.CanRunToEnemyToHit(goalEnemy, out var pathToRun))
                {
                    melee.ShallEndRun = true;
                    return false;
                }

                if (goalEnemy.Distance < owner.Settings.FileSettings.Shoot.MELEE_STOP_MOVE_DISTANCE)
                    owner.StopMove();
                else
                    owner.GoToByWay(pathToRun);
            }
        }
        else
        {
            if (!melee.CanRunToEnemyToHit(goalEnemy, out var pathToRun2))
            {
                melee.ShallEndRun = true;
                return false;
            }

            if (goalEnemy.Distance < owner.Settings.FileSettings.Shoot.MELEE_STOP_MOVE_DISTANCE)
                owner.StopMove();
            else
                owner.GoToByWay(pathToRun2);
        }

        return true;
    }
}
