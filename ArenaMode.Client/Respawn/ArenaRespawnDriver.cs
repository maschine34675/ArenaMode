using ArenaMode.Config;
using ArenaMode.Compat;
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
    private static readonly ArenaRaidEpoch RaidEpoch = new ArenaRaidEpoch();

    public static bool IsRespawnInProgress { get; private set; }
    public static bool StandDown { get; internal set; }
    public static int DeathsThisSession { get; private set; }

    private static bool IsTerminalTdmHold => Modes.ArenaMatchService.Current?.Mode
        is Modes.TeamDeathmatchMode teams && teams.ResultOwnsCurrentRaid;

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
            if (PitFireTeamCompat.HasFrozenTeamRoster)
            {
                Modes.ArenaMatchService.ScheduleFailedStartAbort(
                    "Arena could not identify the original local PMC at death");
                return;
            }

            LocalGameAccessor.InvokeDeathScreen(game);
            return;
        }

        IsRespawnInProgress = true;
        DeathsThisSession++;
        int raidEpoch = RaidEpoch.Capture();
        Plugin.ModLogger.LogInfo(
            PitFireTeamCompat.HasFrozenTeamRoster
                ? $"Arena death #{DeathsThisSession} — held for Tactical round transition."
                : $"Arena death #{DeathsThisSession} — instant respawn.");

        ArenaEvents.RaisePlayerDied(deadPlayer);
        game.StartCoroutine(FixOwnCorpseRenders(deadPlayer));
        game.StartCoroutine(RespawnRoutine(game, deadPlayer, deadPlayer.Position, raidEpoch));
    }

    private static IEnumerator RespawnRoutine(
        LocalGame game,
        LocalPlayer deadPlayer,
        Vector3 corpsePosition,
        int raidEpoch)
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
        while (Modes.ArenaMatchService.Current?.Mode?.HoldRespawn == true
               && IsSameActiveRaid(game, raidEpoch)
               && !StandDown)
            yield return null;

        // HoldRespawn also releases when GameEnd clears the current match. Exit
        // before inspecting the deliberately frozen finalization roster; that
        // roster can outlive the stopped game briefly and must never schedule an
        // abort that later poisons the next ordinary raid.
        if (!IsSameActiveRaid(game, raidEpoch) || StandDown)
        {
            EndPipelineUse(raidEpoch);
            yield break;
        }

        // A frozen PitFireTeam roster may enter Redeploy only through Tactical's
        // one-shot round-transition authorization. That authorization binds the
        // exact LocalGame/dead body, epoch and fully prepared in-memory loadout;
        // a mode drift or premature hold release remains a protected abort.
        if (PitFireTeamCompat.HasFrozenTeamRoster)
        {
            if (!Modes.ArenaMatchService.TryTakeTeamRespawnAuthorization(
                    game,
                    deadPlayer,
                    out TeamArenaPreparedLoadoutRestore prepared,
                    out int teamEpoch,
                    out string authorizationReason)
                || prepared == null
                || teamEpoch <= 0
                || prepared.RoundEpoch != teamEpoch)
            {
                string reason = string.IsNullOrWhiteSpace(authorizationReason)
                    ? "Tactical supplied no complete one-shot respawn authorization"
                    : authorizationReason;
                if (prepared != null && prepared.RoundEpoch != teamEpoch)
                    reason += $" (prepared epoch {prepared.RoundEpoch}, authorized epoch {teamEpoch})";

                AbortProtectedTeamRespawn(
                    raidEpoch,
                    $"the frozen PitFireTeam leader could not be authorized for replacement: {reason}");
                yield break;
            }

            yield return game.StartCoroutine(RespawnProtectedTeamPlayer(
                game,
                deadPlayer,
                corpsePosition,
                raidEpoch,
                prepared,
                teamEpoch));
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

        // A final kill or time limit can land while kit bundles are loading.
        // Keep death interception alive, but never start a new body replacement
        // after the immutable Team DM result. Native stop releases this hold.
        while (IsTerminalTdmHold && IsSameActiveRaid(game, raidEpoch) && !StandDown)
            yield return null;

        // Preload yields across arbitrary frames. GameEnd advances the epoch and
        // clears Arena before the old coroutine resumes, so never enter Redeploy
        // or open UI from a continuation owned by the stopped raid.
        if (!IsSameActiveRaid(game, raidEpoch)
            || StandDown
            || !ReferenceEquals(game.LocalPlayer, deadPlayer))
        {
            EndPipelineUse(raidEpoch);
            yield break;
        }

        var requestedSpawn = new ArenaSpawnSelector(
            new InfilSpawnSelector(new NearPositionSpawnSelector(() => corpsePosition, () => 25f)),
            () => corpsePosition, Settings.RespawnMinDistance.Value);
        if (!Areas.ArenaAreaRespawn.TryPrepare(game, requestedSpawn, out var preparedSpawn))
        {
            EndPipelineUse(raidEpoch);
            yield break;
        }

        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = deadPlayer,
            // Random and away from the body, rather than the engine's "farthest
            // from everyone", which is stable enough to hand out the same corner
            // over and over.
            SpawnSelector = preparedSpawn,
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

        // ResetSession may have invalidated this coroutine while Redeploy was
        // unwinding. Do not clear a newer raid's flags, recover an old body, show
        // a death screen or publish a respawn event across that boundary.
        if (!IsSameActiveRaid(game, raidEpoch) || StandDown)
        {
            EndPipelineUse(raidEpoch);
            yield break;
        }

        if (failure != null)
        {
            if (IsTerminalTdmHold && Modes.ArenaMatchService.Current?.Mode is Modes.TeamDeathmatchMode terminal)
            {
                // The engine may have failed after destroying the old owner.
                // Neither the generic death screen nor area abort verifies that
                // owner before Stop. Keep interception; the result coordinator
                // alone may exit after verifying the actual native owner.
                EndPipelineUse(raidEpoch);
                terminal.ReportResultExitProblem("Player replacement failed; safe exit pending: " + failure.GetBaseException().Message);
                yield break;
            }
            Plugin.ModLogger.LogError($"Arena respawn failed: {failure}");
            StandDown = true;
            EndPipelineUse(raidEpoch);
            if (Areas.ArenaAreaRuntime.CurrentArea != null)
            {
                Areas.ArenaAreaRuntime.Abort("body replacement failed: " + failure.GetBaseException().Message);
                yield break;
            }
            LocalGameAccessor.InvokeDeathScreen(game);
            yield break;
        }

        EndPipelineUse(raidEpoch);

        var now = Time.realtimeSinceStartup;
        Plugin.ModLogger.LogInfo(
            $"Arena respawn timing: kit {kitPickedAt - startedAt:0.00}s, "
            + $"bundles {preloadedAt - kitPickedAt:0.00}s, "
            + $"pipeline {now - preloadedAt:0.00}s, total {now - startedAt:0.00}s.");

        ArenaEvents.RaisePlayerRespawned(RespawnEngineState.LastRespawnedPlayer);
    }

    /// <summary>
    /// The only protected-team entrance to Redeploy. Tactical completed every
    /// deserialization and graph check before releasing HoldRespawn; the engine
    /// callback therefore performs only the prepared transactional commit.
    /// </summary>
    private static IEnumerator RespawnProtectedTeamPlayer(
        LocalGame game,
        LocalPlayer deadPlayer,
        Vector3 corpsePosition,
        int raidEpoch,
        TeamArenaPreparedLoadoutRestore prepared,
        int teamEpoch)
    {
        var startedAt = Time.realtimeSinceStartup;
        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = deadPlayer,
            SpawnSelector = new ArenaSpawnSelector(
                new InfilSpawnSelector(new NearPositionSpawnSelector(() => corpsePosition, () => 25f)),
                () => corpsePosition,
                Settings.RespawnMinDistance.Value),
            EquipmentStrategy = (profile, dead) =>
            {
                if (!prepared.TryCommit(profile, out TeamArenaLoadoutRestoreResult result))
                {
                    string diagnostic = result?.Failure ?? "the prepared restore returned no diagnostic";
                    string rollback = result == null
                        ? "unknown"
                        : result.RollbackComplete.ToString();
                    throw new InvalidOperationException(
                        $"protected team loadout commit failed for round epoch {teamEpoch} "
                        + $"(rollback complete: {rollback}): {diagnostic}");
                }
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
        yield return game.StartCoroutine(RespawnEngine.RunRespawn(request, ex => failure = ex));

        // An external teardown owns the raid now. It may have invalidated the
        // epoch while Redeploy was awaiting player creation, so do not schedule
        // a second Stop or publish a stale body across that boundary.
        if (!IsSameActiveRaid(game, raidEpoch) || StandDown)
        {
            EndPipelineUse(raidEpoch);
            yield break;
        }

        if (failure != null)
        {
            AbortProtectedTeamRespawn(
                raidEpoch,
                $"protected team respawn failed for round epoch {teamEpoch}: "
                + failure.GetBaseException().Message,
                failure);
            yield break;
        }

        LocalPlayer respawnedPlayer = RespawnEngineState.LastRespawnedPlayer;
        if (respawnedPlayer == null
            || !respawnedPlayer.IsYourPlayer
            || !ReferenceEquals(game.LocalPlayer, respawnedPlayer))
        {
            AbortProtectedTeamRespawn(
                raidEpoch,
                $"protected team respawn for round epoch {teamEpoch} did not publish the exact new local-player body");
            yield break;
        }

        EndPipelineUse(raidEpoch);
        Plugin.ModLogger.LogInfo(
            $"Arena protected team respawn completed for round epoch {teamEpoch} "
            + $"in {Time.realtimeSinceStartup - startedAt:0.00}s.");
        ArenaEvents.RaisePlayerRespawned(respawnedPlayer);
    }

    private static void AbortProtectedTeamRespawn(
        int raidEpoch,
        string reason,
        Exception failure = null)
    {
        EndPipelineUse(raidEpoch);
        StandDown = true;

        if (failure == null)
            Plugin.ModLogger.LogError($"Arena blocked protected team respawn: {reason}");
        else
            Plugin.ModLogger.LogError(
                $"Arena protected team respawn failed ({reason}): {failure}");

        // The shared coordinator keeps both Arena and PitFireTeam persistence
        // guards active and retries native Stop if the game is not ready yet.
        Modes.ArenaMatchService.ScheduleFailedStartAbort(reason);
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
        => ArenaSession.IsActive && (IsRespawnInProgress || IsTerminalTdmHold) && !StandDown;

    public bool ShouldBlockKilledStop(LocalGame game)
        => ArenaSession.IsActive && (IsRespawnInProgress || IsTerminalTdmHold) && !StandDown;

    public void OnExternalStop()
    {
        if (!IsRespawnInProgress)
            return;

        Plugin.ModLogger.LogWarning("External raid end during arena respawn — standing down.");
        StandDown = true;
    }

    public bool ShouldHoldBlackScreen()
        => ArenaSession.IsActive && IsRespawnInProgress && !StandDown
           && (!IsTerminalTdmHold || RespawnEngineState.IsPipelineRunning);

    public bool ShouldSuppressKilledSessionResult()
        => ArenaSession.IsActive && (IsRespawnInProgress || IsTerminalTdmHold) && !StandDown;

    public void OnRaidEnded() => ResetSession();

    public bool ShouldGuardOwnerCleanup() => IsRespawnInProgress;

    internal static void ResetSession()
    {
        // FIRST: every coroutine captured before this boundary becomes unable to
        // touch the player, global flags or UI even if its awaited EFT task later
        // completes in a new raid.
        RaidEpoch.Advance();
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
        Bots.TeamArenaPursuit.Reset();
        Combat.ArenaGiveUp.Reset();
        Combat.WeaponSwapService.Reset();
        ArenaSpawnSelector.Reset();
        KitCatalog.KitProvider = null;
        LadderCatalog.Reset();

        // The summaries describe kits from the catalogue that is about to be
        // re-fetched, so they expire with it.
        KitSummary.Reset();
    }

    internal static int CaptureRaidEpoch() => RaidEpoch.Capture();

    internal static bool IsSameActiveRaid(LocalGame game, int raidEpoch)
        => RaidEpoch.IsCurrent(
            raidEpoch,
            ArenaSession.IsActive,
            game != null && game.Status == GameStatus.Started);

    internal static bool BeginPipelineUse(LocalGame game, int raidEpoch)
    {
        if (!IsSameActiveRaid(game, raidEpoch)
            || StandDown
            || IsRespawnInProgress)
        {
            return false;
        }

        IsRespawnInProgress = true;
        return true;
    }

    internal static void EndPipelineUse(int raidEpoch)
    {
        if (RaidEpoch.IsEpochCurrent(raidEpoch))
            IsRespawnInProgress = false;
    }
}
