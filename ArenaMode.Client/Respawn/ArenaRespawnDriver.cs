using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Kits;
using ArenaMode.Session;
using EFT;
using HarmonyLib;
using Redeploy.Core.Engine;
using Redeploy.Core.GameAccess;
using Redeploy.Core.Spawning;
using System;
using System.Collections;
using UnityEngine;

namespace ArenaMode.Respawn;

/// <summary>
/// Arena's claim on the shared death-interception patch set: instant respawn with
/// the selected kit, no prompt. Priority 100 — outranks Redeploy's prompt flow in
/// arena raids; inert everywhere else (IsActive gate).
/// </summary>
internal sealed class ArenaRespawnDriver : IDeathInterceptionDriver
{
    private static readonly AccessTools.FieldRef<Player, EFT.Interactive.Corpse> CorpseField =
        AccessTools.FieldRefAccess<Player, EFT.Interactive.Corpse>("Corpse");

    public static bool IsRespawnInProgress { get; private set; }
    public static bool StandDown { get; internal set; }
    public static int DeathsThisSession { get; private set; }

    public string Name => "ArenaMode";

    public int Priority => 100;

    public bool WantsToHandleDeath(LocalGame game)
    {
        return ArenaSession.IsActive
               && !StandDown
               && !IsRespawnInProgress
               && game?.Location?.Id != "hideout";
    }

    public void OnPlayerDied(LocalGame game)
    {
        var deadPlayer = game.LocalPlayer;
        if (deadPlayer == null || !deadPlayer.IsYourPlayer)
        {
            StandDown = true;
            LocalGameAccessor.InvokeDeathScreen(game);
            return;
        }

        IsRespawnInProgress = true;
        DeathsThisSession++;
        Plugin.ModLogger.LogInfo($"Arena death #{DeathsThisSession} — instant respawn.");

        ArenaEvents.RaisePlayerDied(deadPlayer);
        game.StartCoroutine(FixOwnCorpseRenders(deadPlayer));
        game.StartCoroutine(RespawnRoutine(game, deadPlayer.Position));
    }

    private static IEnumerator RespawnRoutine(LocalGame game, Vector3 corpsePosition)
    {
        // A mode may keep the player down. Tactical does: death costs you the
        // round, and the next one is when you come back. Waited out BEFORE the
        // kit is chosen, so whatever gets bought during the wait is what the
        // player is handed — picking first would hand them the loadout they had
        // when they died.
        //
        // The mode is re-read every frame rather than captured: it is a per-raid
        // object, and holding a reference across a wait of unknown length is how
        // a mode that ended keeps a player dead.
        while (Modes.ArenaMatchService.Current?.Mode?.HoldRespawn == true)
            yield return null;

        // The loop ALSO exits when the match is gone, because Current then
        // null-propagates to false — and that is a raid ending, not a release.
        // Without this the routine would wake up and run the whole respawn
        // pipeline into a raid already in teardown, with the owner-cleanup guard
        // switched off. LiveKitRespawn re-checks after its waits for exactly this
        // reason; this wait is unbounded and had no equivalent.
        //
        // Status rather than StandDown: by the time the coroutine resumes,
        // ResetSession has already cleared StandDown.
        if (game == null || game.Status != GameStatus.Started || !ArenaSession.IsActive)
        {
            IsRespawnInProgress = false;
            yield break;
        }

        // Timed in three parts because the whole thing grows: measured from a
        // playtest log, death to new gear went from 0.09s early in a raid to
        // 10.9s by minute twenty-three. Corpses looked like the culprit and were
        // not — clearing them changed nothing — so the next guess gets measured
        // rather than argued about. Whichever number climbs is the answer.
        var startedAt = Time.realtimeSinceStartup;

        var kit = KitCatalog.GetSelectedKit();
        var kitPickedAt = Time.realtimeSinceStartup;

        // No-op when this kit was already preloaded this raid (raid-start swap);
        // covers a mid-raid kit switch via F12.
        yield return KitBundlePreloader.Preload(kit);
        var preloadedAt = Time.realtimeSinceStartup;

        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = game.LocalPlayer,
            // Random and away from the body, rather than the engine's "farthest
            // from everyone", which is stable enough to hand out the same corner
            // over and over.
            SpawnSelector = new ArenaSpawnSelector(
                new InfilSpawnSelector(new NearPositionSpawnSelector(() => corpsePosition, () => 25f)),
                () => corpsePosition,
                Settings.RespawnMinDistance.Value),
            EquipmentStrategy = (profile, dead) =>
            {
                if (kit != null)
                    KitBuilder.ApplyToProfile(profile, dead, kit, requireCorpse: true);
                else
                    Redeploy.Core.Inventory.EquipmentSurgeon.ReplaceEquipment(profile, dead);
            },
            HealthPercent = 100f,
            Protection = new SpawnProtectionOptions
            {
                DurationSeconds = Settings.SpawnProtectionSeconds.Value,
                EndEarlyOnWeapon = false,
                StowAutoDrawnWeapon = false,
                ShowToasts = true,
            },
        };

        Exception failure = null;
        yield return game.StartCoroutine(RespawnEngine.RunRespawn(request, e => failure = e));

        if (failure != null)
        {
            Plugin.ModLogger.LogError($"Arena respawn failed: {failure}");
            StandDown = true;
            IsRespawnInProgress = false;
            LocalGameAccessor.InvokeDeathScreen(game);
            yield break;
        }

        IsRespawnInProgress = false;

        var now = Time.realtimeSinceStartup;
        Plugin.ModLogger.LogInfo(
            $"Arena respawn timing: kit {kitPickedAt - startedAt:0.00}s, "
            + $"bundles {preloadedAt - kitPickedAt:0.00}s, "
            + $"pipeline {now - preloadedAt:0.00}s, total {now - startedAt:0.00}s.");

        ArenaEvents.RaisePlayerRespawned(RespawnEngineState.LastRespawnedPlayer);
    }

    /// <summary>
    /// The corpse reuses the first-person body whose torso renders shadows-only;
    /// switch to third-person renders like the game does for world corpses.
    /// </summary>
    private static IEnumerator FixOwnCorpseRenders(LocalPlayer deadPlayer)
    {
        var timeout = Time.time + 10f;
        while (deadPlayer != null && CorpseField(deadPlayer) == null && Time.time < timeout)
            yield return null;

        if (deadPlayer == null || CorpseField(deadPlayer) == null)
            yield break;

        try
        {
            deadPlayer.PlayerBody?.UpdatePlayerRenders(EPointOfView.ThirdPerson, deadPlayer.Side);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena corpse render fix failed: {ex.Message}");
        }
    }

    public bool ShouldBlockDeathScreen(LocalGame game)
        => ArenaSession.IsActive && IsRespawnInProgress && !StandDown;

    public bool ShouldBlockKilledStop(LocalGame game)
        => ArenaSession.IsActive && IsRespawnInProgress && !StandDown;

    public void OnExternalStop()
    {
        if (!IsRespawnInProgress)
            return;

        Plugin.ModLogger.LogWarning("External raid end during arena respawn — standing down.");
        StandDown = true;
    }

    public bool ShouldHoldBlackScreen()
        => ArenaSession.IsActive && IsRespawnInProgress && !StandDown;

    public bool ShouldSuppressKilledSessionResult()
        => ArenaSession.IsActive && IsRespawnInProgress && !StandDown;

    public void OnRaidEnded() => ResetSession();

    public bool ShouldGuardOwnerCleanup() => IsRespawnInProgress;

    internal static void ResetSession()
    {
        IsRespawnInProgress = false;
        StandDown = false;
        DeathsThisSession = 0;
        KitBundlePreloader.Reset();
        Bots.BurstSpawnService.Reset();
        Bots.CorpseCleanupService.Reset();
        Bots.CorpseLightService.Reset();
        Bots.CorpseWeaponRegistry.Reset();
        Bots.ArenaHostility.Reset();
        Bots.ArenaBotPatches.ResetRaidState();
        Combat.ArenaGiveUp.Reset();
        Combat.WeaponSwapService.Reset();
        ArenaSpawnSelector.Reset();
        KitCatalog.KitProvider = null;
        LadderCatalog.Reset();

        // The summaries describe kits from the catalogue that is about to be
        // re-fetched, so they expire with it.
        KitSummary.Reset();
    }

    internal static void BeginPipelineUse() => IsRespawnInProgress = true;

    internal static void EndPipelineUse() => IsRespawnInProgress = false;
}
