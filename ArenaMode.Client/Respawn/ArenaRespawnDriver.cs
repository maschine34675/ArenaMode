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
        var kit = KitCatalog.GetSelectedKit();

        // No-op when this kit was already preloaded this raid (raid-start swap);
        // covers a mid-raid kit switch via F12.
        yield return KitBundlePreloader.Preload(kit);

        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = game.LocalPlayer,
            SpawnSelector = new InfilSpawnSelector(
                new NearPositionSpawnSelector(() => corpsePosition, () => 25f)),
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
        Bots.ArenaHostility.Reset();
        Bots.ArenaBotPatches.ResetRaidState();
    }

    internal static void BeginPipelineUse() => IsRespawnInProgress = true;

    internal static void EndPipelineUse() => IsRespawnInProgress = false;
}
