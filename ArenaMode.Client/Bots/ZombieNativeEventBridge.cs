using ArenaMode.Config;
using ArenaMode.Modes;
using ArenaMode.Session;
using ArenaMode.Shared;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ArenaMode.Bots;

/// <summary>
/// Bridges Zombie mode onto EFT's own Halloween-2024 infection event, so a
/// zombie that sees or is hit by the player calls every other connected
/// zombie toward the last seen position instead of relying on
/// ZombieSpawnService's own 5-second idle nudge alone.
///
/// Four Harmony hooks, all decompile-verified against the facts the
/// orchestrator's design records:
///
/// 1. Prefix on BotsEventsController.Activate(): repairs the construction-time
///    NULL on BotHalloweenWithZombies._spawner (and its _crowdSpawn's own
///    copy) from the controller's own BotSpawner, THEN — only if that repair
///    found a real spawner — flips the static
///    BotInternalSettingsController.Core.ACTIVE_HALLOWEEN_ZOMBIES_EVENT flag
///    on, remembering its previous value. BotsController's constructor passes
///    its own still-null _botSpawner field into `new BotsEventsController(...)`
///    (EFT\BotsController.cs:211) — _botSpawner is only assigned six lines
///    later (line 236) — so without this repair,
///    BotHalloweenWithZombies.Activate() would NRE inside AddBackupProfiles()
///    (BotHalloweenWithZombies.cs:188-211) the moment the flag turned the
///    native body on. BotsController.Init calls EventsController.Activate()
///    at line 271, still inside LocalGame.vmethod_1 (the SAME loading-time
///    window ArenaProfilePreloadPatch's own doc comment documents), so this
///    gates like that patch does: ArenaPopulationPolicy.IsArmedZombieLoading,
///    not IsActive — IsActive cannot have latched yet.
/// 2. Postfix on BotsEventsController.Dispose(): restores the flag to the
///    value hook 1 saved, idempotently (BotsController.StopGettingInfo calls
///    EventsController.Dispose() at EFT\BotsController.cs:643, at raid
///    teardown — a different moment than the other three hooks, well after
///    IsActive may have latched, so this one carries NO gate of its own: it
///    only ever acts when hook 1 actually saved a value).
/// 3. Prefix on the STATIC BotHalloweenWithZombies.GetProfilesOnStart():
///    replaces the native 498-profile dump (BotHalloweenWithZombies.cs:
///    392-407) with Zombie's own small opening-wave list
///    (ZombieProfilePlan.OpeningProfiles). LocalGame.vmethod_1 calls this
///    UNCONDITIONALLY on InfectionPercentage &gt; 0 (EFT\LocalGame.cs:166-169) —
///    independent of the ACTIVE_HALLOWEEN_ZOMBIES_EVENT flag entirely, and
///    independent of whether hook 1's repair succeeds — so this hook's own
///    gate (the same IsArmedZombieLoading check) is what keeps it from firing
///    on any raid but the exact armed Zombie one currently loading.
/// 4. Prefix on the INSTANCE BotHalloweenWithZombies.AddBackupProfiles():
///    skips the native 120-profile standing order (4 roles x 3 difficulties x
///    10, BotHalloweenWithZombies.cs:188-211) that this method would
///    otherwise register — ZombieMode's own, much smaller,
///    counts-actually-used targets are registered instead at match start
///    (ZombieSpawnService.RegisterBackupTargets). This native body is only
///    ever reached from BotHalloweenWithZombies.Activate(), itself only
///    called when the flag is on — i.e. only when hook 1's repair already
///    succeeded — but it carries the same gate anyway, on the same
///    bit-identical-normal-raids principle every other Arena patch follows
///    (AGENTS.md): a flag left stuck on by a missed restore must never change
///    a non-Arena raid's behaviour either.
///
/// Every reflected member this file touches turned out to be public on the
/// decompiled source (BotsEventsController.BotHalloweenWithZombies is a
/// get-only AUTO property; BotHalloweenWithZombies._spawner/_crowdSpawn and
/// BotHalloweenCrowdSpawn._spawner are public [NonSerialized] fields;
/// BotInternalSettingsController.Core and BotGlobalsCoreSettings.
/// ACTIVE_HALLOWEEN_ZOMBIES_EVENT are a public static field and a public
/// instance field) — the same loose-field style BurstSpawnService and
/// ArenaHostility already rely on elsewhere in this repo (e.g.
/// `spawner._bots.BotOwners`). AccessTools is still used for every Harmony
/// TARGET METHOD below, exactly like every other patch in this codebase;
/// nothing here needed AccessTools.Field, because nothing it touches is
/// non-public. Each hook is enabled independently (TryEnable), so a future
/// game build breaking ONE of these four — a rename, a signature change —
/// leaves the other three installed rather than taking the whole bridge down
/// (see ArenaProfilePreloadPatch.EnablePatch for the one-patch precedent this
/// extends to four). Every hook ALSO fails closed internally: an exception or
/// a missing dependency inside a hook body leaves the native event off and
/// Zombie spawns/fights exclusively through ZombieSpawnService's own
/// awareness steps — never half-armed, never a changed non-Zombie raid.
/// </summary>
internal static class ZombieNativeEventBridge
{
    /// <summary>
    /// The native flag's value before hook 1 touched it, so hook 2 (and the
    /// raid-end safety net below) can put it back. Null means "not currently
    /// ours to restore" — either hook 1 never ran this raid, or it already
    /// restored. One static slot is enough because EFT runs one local raid's
    /// BotsEventsController lifecycle (Activate once, Dispose once) at a time,
    /// the same assumption ArenaSession itself makes with its own single
    /// _startedGame/_startedWorld pair.
    /// </summary>
    private static bool? _savedFlag;

    /// <summary>
    /// Diagnostics only (nothing else reads this yet): null until hook 1 is
    /// attempted for an armed Zombie raid, then true/false for whether the
    /// repair succeeded and the native event is actually armed this raid.
    /// </summary>
    internal static bool? NativeEventArmed { get; private set; }

    internal static void EnablePatch()
    {
        TryEnable(() => new GetProfilesOnStartPatch().Enable(), "the opening-profile preload replacement");
        TryEnable(() => new AddBackupProfilesPatch().Enable(), "the native backup-profile order skip");
        TryEnable(() => new DisposePatch().Enable(), "the native-flag restore on raid teardown");
        TryEnable(() => new ActivatePatch().Enable(), "the native-flag arm on raid activation");
    }

    /// <summary>
    /// Idempotent: safe to call from both BotsEventsController.Dispose's
    /// postfix and Arena's own raid-end path (ArenaSession.OnRaidEnded) as a
    /// safety net — whichever runs first restores the flag and clears
    /// _savedFlag, so the second call finds nothing to do.
    /// </summary>
    internal static void RestoreFlag()
    {
        if (!_savedFlag.HasValue)
            return;

        try
        {
            BotInternalSettingsController.Core.ACTIVE_HALLOWEEN_ZOMBIES_EVENT = _savedFlag.Value;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena zombie native bridge: restoring the native event flag failed: {ex.Message}");
        }
        finally
        {
            _savedFlag = null;
            NativeEventArmed = null;
        }
    }

    private static void TryEnable(Action enable, string what)
    {
        try
        {
            enable();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning(
                $"Arena zombie native bridge: {what} is unavailable ({ex.Message}) — that hook stays inert; "
                + "Zombie mode is unaffected, it falls back to ZombieSpawnService's own awareness path.");
        }
    }

    private static bool IsArmedZombieLoading()
        => ArenaPopulationPolicy.IsArmedZombieLoading(
            ArenaSession.ArmedForNextRaid, ArenaSession.ArmedMap, ArenaSession.ArmedMode, CurrentLoadingMap());

    private static string CurrentLoadingMap()
        => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance?.LocationId : null;

    /// <summary>
    /// Null (never touches the flag) unless the controller's own BotSpawner
    /// can be read right now. Its static return type (BotSpawner) is the type
    /// verification decision 3 asked for — the compiler already pins it, so
    /// the only runtime check left is the null guard below.
    /// </summary>
    private static bool TryRepairSpawner(BotHalloweenWithZombies bhwz)
    {
        if (bhwz == null)
            return false;

        BotSpawner spawner = Singleton<IBotGame>.Instantiated
            ? Singleton<IBotGame>.Instance?.BotsController?.BotSpawner
            : null;
        if (spawner == null)
            return false;

        bhwz._spawner = spawner;

        // Shares the same construction-time null (BotHalloweenWithZombies'
        // ctor hands its own spawner parameter straight to
        // BotHalloweenCrowdSpawn's ctor). With MaxCrowdAttackSpawnLimit=0 and
        // CrowdsLimit=0 (ZombieLocationSettings) its DoSpawn loop never
        // actually dereferences _spawner — CalcBotsCanSpawnedPerCrowd returns
        // 0, so BeginCrowdSpawn's spawnsRemains is 0 and DoSpawn's while loop
        // never runs — but repair it anyway, exactly as decision 3a asks,
        // rather than betting the mode's safety on those two zeros forever.
        if (bhwz._crowdSpawn != null)
            bhwz._crowdSpawn._spawner = spawner;

        return true;
    }

    /// <summary>Converts ZombieMode's opening-wave plan into the native CountTypeBotWave
    /// list GetProfilesOnStartPatch hands back as __result.</summary>
    private static List<CountTypeBotWave> BuildOpeningProfiles()
    {
        var entries = ZombieProfilePlan.OpeningProfiles(
            Settings.ZombieWaveStartSize.Value, Settings.ZombieShooterPercent.Value);

        var waves = new List<CountTypeBotWave>(entries.Count);
        foreach (var entry in entries)
        {
            if (ZombieSpawnService.TryMapRole(entry.Role, out var role)
                && ZombieSpawnService.TryMapDifficulty(entry.Difficulty, out var difficulty))
            {
                waves.Add(new CountTypeBotWave(entry.Count, role, difficulty));
            }
        }

        return waves;
    }

    private sealed class ActivatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotsEventsController), nameof(BotsEventsController.Activate));

        [PatchPrefix]
        private static void Prefix(BotsEventsController __instance)
        {
            try
            {
                if (!IsArmedZombieLoading())
                    return;

                if (_savedFlag.HasValue)
                {
                    // A previous Activate ran without a matching Dispose/restore.
                    // Never overwrite the TRUE original a second time, or
                    // restoring later would set the flag to what THIS raid saw
                    // rather than what it genuinely was before any of this.
                    Plugin.ModLogger.LogWarning(
                        "Arena zombie native bridge: Activate() fired again before the previous raid's flag "
                        + "was restored — leaving the saved original value alone.");
                }
                else
                {
                    _savedFlag = BotInternalSettingsController.Core.ACTIVE_HALLOWEEN_ZOMBIES_EVENT;
                }

                if (!TryRepairSpawner(__instance.BotHalloweenWithZombies))
                {
                    NativeEventArmed = false;
                    Plugin.ModLogger.LogWarning(
                        "Arena zombie native bridge: could not repair BotHalloweenWithZombies' null _spawner "
                        + "(no BotSpawner available yet) — leaving the native Halloween event OFF. Zombies "
                        + "still spawn and fight on sight through ZombieSpawnService; only the native "
                        + "pursuit/call relay is lost.");
                    return;
                }

                BotInternalSettingsController.Core.ACTIVE_HALLOWEEN_ZOMBIES_EVENT = true;
                NativeEventArmed = true;
                Plugin.ModLogger.LogInfo(
                    "Arena zombie native bridge: armed the native Halloween infection event for this raid.");
            }
            catch (Exception ex)
            {
                NativeEventArmed = false;
                Plugin.ModLogger.LogWarning(
                    $"Arena zombie native bridge: Activate hook failed ({ex.Message}) — native event left OFF.");
            }
        }
    }

    private sealed class DisposePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotsEventsController), nameof(BotsEventsController.Dispose));

        [PatchPostfix]
        private static void Postfix() => RestoreFlag();
    }

    private sealed class GetProfilesOnStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotHalloweenWithZombies), nameof(BotHalloweenWithZombies.GetProfilesOnStart));

        [PatchPrefix]
        private static bool Prefix(ref IEnumerable<CountTypeBotWave> __result)
        {
            if (!IsArmedZombieLoading())
                return true;

            try
            {
                __result = BuildOpeningProfiles();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.ModLogger.LogWarning(
                    $"Arena zombie native bridge: opening-profile replacement failed ({ex.Message}) — "
                    + "the native 498-profile preload runs instead this raid.");
                return true;
            }
        }
    }

    private sealed class AddBackupProfilesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BotHalloweenWithZombies), nameof(BotHalloweenWithZombies.AddBackupProfiles));

        [PatchPrefix]
        private static bool Prefix()
        {
            if (!IsArmedZombieLoading())
                return true;

            // Skip the native 120-profile standing order (4 roles x 3
            // difficulties x 10). ZombieMode registers its own, much smaller,
            // counts-actually-used targets instead at match start (decision
            // 3d; ZombieSpawnService.RegisterBackupTargets). This also keeps
            // the native body's own _spawner dereference from ever running,
            // on top of hook 1's repair.
            return false;
        }
    }
}
