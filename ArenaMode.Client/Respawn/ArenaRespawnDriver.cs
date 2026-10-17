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

    private static bool IsTerminalTdmHold => Modes.ArenaMatchResult.OwnsRaid(Modes.ArenaMatchService.Current?.Mode);

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
        Plugin.ModLogger.LogDebug(
            PitFireTeamCompat.HasFrozenTeamRoster
                ? $"Arena death #{DeathsThisSession} — held for protected team transition."
                : Modes.ArenaMatchService.Current?.Mode is Modes.ZombieMode { IsTeamArena: false }
                    ? $"Arena death #{DeathsThisSession} — solo survival ended; no respawn."
                : ArenaSession.ReplaySeconds > 0f
                    ? $"Arena death #{DeathsThisSession} — respawn after optional killcam."
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
        // Bind this exact match/body, then recheck its identity every frame.
        // Replay ownership and the mode hold run concurrently; neither may
        // release an old continuation into a later match or a new living body.
        ArenaDeathReplayHold replayHold = null;
        if (ArenaSession.ReplaySeconds > 0f)
        {
            replayHold = new ArenaDeathReplayHold(game, deadPlayer, raidEpoch);
            yield return replayHold.Wait();
            if (!replayHold.CanRespawn)
            {
                EndPipelineUse(raidEpoch);
                yield break;
            }
        }
        // Keep the no-replay path and its coroutine timing unchanged.
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

        // Zombie's own-gear option (item 4, 2026-10-06 follow-up): the same
        // idea as the frozen PitFireTeam branch just below, for solo play —
        // restore the entry loadout instead of drawing a kit. Frozen PFT teams
        // are explicitly excluded: they must consume the prepared lease below.
        // Checked FIRST for the solo path
        // (and unconditionally skips KitCatalog.GetSelectedKit() below) so a
        // random-kit respawn can never reach a player who asked to keep their
        // own gear. See ZombieOwnGearLoadout's own doc comment for why no
        // separate "prepare ahead of time" stage is needed, unlike the team
        // branch's TickTeamRetirement staging.
        if (ArenaSession.ZombieOwnGearActive && !PitFireTeamCompat.HasFrozenTeamRoster)
        {
            yield return game.StartCoroutine(RespawnOwnGearPlayer(game, deadPlayer, corpsePosition, raidEpoch));
            yield break;
        }

        // A frozen PitFireTeam roster may enter Redeploy only through its mode's
        // one-shot round/wave-transition authorization. That authorization binds the
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
                    ? "The protected team supplied no complete one-shot respawn authorization"
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

        // Timed in parts because the whole thing grows: measured from a
        // playtest log, death to new gear went from 0.09s early in a raid to
        // 10.9s by minute twenty-three. Corpses looked like the culprit and were
        // not — clearing them changed nothing — so the next guess gets measured
        // rather than argued about. Whichever number climbs is the answer.
        //
        // "pipeline" is the RunRespawn call alone, so it can be set against
        // Redeploy Core's own "Respawn pipeline #N" line; what Core does outside
        // its stages (its preflight, reading its counters, writing that line)
        // is the difference. "prepare" is everything Arena does between the
        // bundles and that call: the selector, the area preflight and the
        // request. The preflight resolves the spawn point only while an area is
        // active; without one the selector runs inside Core's "spawn" group.
        // The terminal result hold (Team DM and Deathmatch rounds) is a wait,
        // not work: it is in neither, and appears as "hold" only on the rare
        // line that actually waited. Until 17.09.2026 "pipeline" was hold,
        // prepare and pipeline together.
        // Stopwatch rather than Unity's clock, and spelled out, because
        // tools/TdmMatchChecks compiles the hold below outside Unity and
        // without this file's usings.
        long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        var kit = KitCatalog.GetSelectedKit();
        long kitPickedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // No-op when this kit was already preloaded this raid (raid-start swap);
        // covers a mid-raid kit switch via F12.
        yield return KitBundlePreloader.Preload(kit);
        long preloadedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // Preload can yield after the first release. Revalidate both mode and
        // optional camera ownership before any native owner is destroyed.
        if (replayHold != null)
        {
            yield return replayHold.Wait();
            if (!replayHold.CanRespawn)
            {
                EndPipelineUse(raidEpoch);
                yield break;
            }
        }

        // A final kill or time limit can land while kit bundles are loading.
        // Keep death interception alive, but never start a new body replacement
        // after the immutable Team DM result. Native stop releases this hold.
        bool held = false;
        while (IsTerminalTdmHold && IsSameActiveRaid(game, raidEpoch) && !StandDown)
        {
            held = true;
            yield return null;
        }
        long releasedAt = System.Diagnostics.Stopwatch.GetTimestamp();

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
        long pipelineStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        yield return game.StartCoroutine(RespawnEngine.RunRespawn(request, e => failure = e));
        long pipelineEndedAt = System.Diagnostics.Stopwatch.GetTimestamp();

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
            if (IsTerminalTdmHold)
            {
                // The engine may have failed after destroying the old owner.
                // Neither the generic death screen nor area abort verifies that
                // owner before Stop. Keep interception; the result coordinator
                // alone may exit after verifying the actual native owner.
                EndPipelineUse(raidEpoch);
                Modes.ArenaMatchResult.ReportProblem(Modes.ArenaMatchService.Current?.Mode, "Player replacement failed; safe exit pending: " + failure.GetBaseException().Message);
                yield break;
            }
            Plugin.ModLogger.LogError($"Arena respawn failed: {failure}");
            StandDown = true;
            EndPipelineUse(raidEpoch);
            // Box respawns retain their safe-exit owner after a failed handover.
            if (Bots.ArenaTeamBoxRuntime.IsRequired)
            {
                Modes.ArenaMatchService.ScheduleFailedStartAbort(
                    "Arena team body replacement failed: " + failure.GetBaseException().Message);
                yield break;
            }
            if (Areas.ArenaAreaRuntime.CurrentArea != null)
            {
                Areas.ArenaAreaRuntime.Abort("body replacement failed: " + failure.GetBaseException().Message);
                yield break;
            }
            LocalGameAccessor.InvokeDeathScreen(game);
            yield break;
        }

        EndPipelineUse(raidEpoch);

        ArenaMode.Diagnostics.ArenaLog.Developer(
            $"Arena respawn timing: kit {Seconds(startedAt, kitPickedAt):0.00}s, "
            + $"bundles {Seconds(kitPickedAt, preloadedAt):0.00}s, "
            + (held ? $"hold {Seconds(preloadedAt, releasedAt):0.00}s, " : "")
            + $"prepare {Seconds(releasedAt, pipelineStartedAt):0.00}s, "
            + $"pipeline {Seconds(pipelineStartedAt, pipelineEndedAt):0.00}s, "
            + $"total {Seconds(startedAt, pipelineEndedAt):0.00}s.");

        // Publish only the body the engine produced for THIS request, checked the way
        // the protected path checks it: LastRespawnedPlayer is engine-global, and the
        // mode framework must never see it stale or as a body the game no longer holds.
        LocalPlayer respawned = RespawnEngineState.LastRespawnedPlayer;
        if (respawned == null || !respawned.IsYourPlayer || !ReferenceEquals(game.LocalPlayer, respawned))
        {
            Plugin.ModLogger.LogWarning("Arena respawn finished without the exact new local-player body — no respawn event published.");
            if (Bots.ArenaTeamBoxRuntime.IsRequired)
                Modes.ArenaMatchService.ScheduleFailedStartAbort("Arena team-box player replacement did not return the exact local body.");
            yield break;
        }

        // Body creation is asynchronous. Revalidate the actual arrival before
        // any kit/respawn subscriber can resume the match or release its hold.
        if (!Bots.ArenaTeamBoxRuntime.ReleasePlayerReservation(respawned))
            yield break;

        if (kit != null)
            ArenaEvents.RaiseKitApplied(kit);
        ArenaEvents.RaisePlayerRespawned(respawned);
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
                // The dead body's keys and items are still exact here. What
                // the last second before the death did to them — a key
                // cleared by hand, a medkit used up — is judged now, before
                // the rebuild renames every ordinary item. Never throws.
                Kits.AutoQuickSlots.SettleBeforeReissue(profile);

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

                // The ordinary items now carry fresh ids and the bindings point
                // at them. The settled ledger follows the same map, so every
                // owned key whose occupant the map knows is owned under its new
                // id from the first poll on the new body; an occupant the map
                // does not know (picked up mid-round, so not in the entry
                // loadout) loses its binding in the rebuild and reads as gone
                // on that poll, which refills the key. Never throws.
                Kits.AutoQuickSlots.NoteItemsReissued(prepared.NextRuntimeByPreviousRuntimeId);
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
        Plugin.ModLogger.LogDebug(
            $"Arena protected team respawn completed for round epoch {teamEpoch} "
            + $"in {Time.realtimeSinceStartup - startedAt:0.00}s.");
        ArenaEvents.RaisePlayerRespawned(respawnedPlayer);
    }

    /// <summary>
    /// Zombie's own-gear option (item 4): the only non-team entrance to
    /// Redeploy that restores the entry loadout instead of a kit. Arm+
    /// Prepare+Commit all run inside the EquipmentStrategy callback itself —
    /// see ZombieOwnGearLoadout's own doc comment for why no separate
    /// staging phase is needed here, unlike RespawnProtectedTeamPlayer's
    /// team equivalent above. Area-bounded spawn selection matches the
    /// ordinary (non-team) respawn path above, since Zombie is solo like it,
    /// not team like PitFireTeam.
    /// </summary>
    private static IEnumerator RespawnOwnGearPlayer(
        LocalGame game,
        LocalPlayer deadPlayer,
        Vector3 corpsePosition,
        int raidEpoch)
    {
        var startedAt = Time.realtimeSinceStartup;

        var requestedSpawn = new ArenaSpawnSelector(
            new InfilSpawnSelector(new NearPositionSpawnSelector(() => corpsePosition, () => 25f)),
            () => corpsePosition, Settings.RespawnMinDistance.Value);
        if (!Areas.ArenaAreaRespawn.TryPrepare(game, requestedSpawn, out var preparedSpawn))
        {
            EndPipelineUse(raidEpoch);
            yield break;
        }

        string restoreReason = null;
        int restoreEpoch = 0;
        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = deadPlayer,
            SpawnSelector = preparedSpawn,
            EquipmentStrategy = (profile, dead) =>
            {
                // The dead body's keys and items are still exact here — same
                // reasoning as RespawnProtectedTeamPlayer's own comment.
                Kits.AutoQuickSlots.SettleBeforeReissue(profile);

                if (!Kits.ZombieOwnGearLoadout.TryRestore(
                        profile, out restoreEpoch, out restoreReason, out var nextRuntimeMap))
                {
                    throw new InvalidOperationException(
                        $"Zombie own-gear respawn could not restore the entry loadout: {restoreReason}");
                }

                Kits.AutoQuickSlots.NoteItemsReissued(nextRuntimeMap);
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

        if (!IsSameActiveRaid(game, raidEpoch) || StandDown)
        {
            EndPipelineUse(raidEpoch);
            yield break;
        }

        if (failure != null)
        {
            AbortOwnGearRespawn(
                raidEpoch, $"Zombie own-gear respawn failed: {failure.GetBaseException().Message}");
            yield break;
        }

        EndPipelineUse(raidEpoch);

        LocalPlayer respawned = RespawnEngineState.LastRespawnedPlayer;
        if (respawned == null || !respawned.IsYourPlayer || !ReferenceEquals(game.LocalPlayer, respawned))
        {
            Plugin.ModLogger.LogWarning(
                "Arena Zombie own-gear respawn finished without the exact new local-player body — no respawn event published.");
            yield break;
        }

        Plugin.ModLogger.LogDebug(
            $"Arena Zombie own-gear respawn completed (epoch {restoreEpoch}) "
            + $"in {Time.realtimeSinceStartup - startedAt:0.00}s.");
        ArenaEvents.RaisePlayerRespawned(respawned);
    }

    private static void AbortOwnGearRespawn(int raidEpoch, string reason)
    {
        EndPipelineUse(raidEpoch);
        StandDown = true;
        Plugin.ModLogger.LogError($"Arena blocked Zombie own-gear respawn: {reason}");
        Modes.ArenaMatchService.ScheduleFailedStartAbort(reason);
    }

    private static double Seconds(long fromTimestamp, long toTimestamp)
        => (toTimestamp - fromTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency;

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

        // Protected result/leave coordinators stand down before invoking native
        // Stop. Only a stop that interrupts an active respawn is unexpected.
        if (StandDown)
            Plugin.ModLogger.LogDebug("Arena native stop observed after respawn already stood down.");
        else
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
        Bots.BotLoadBoost.Reset();
        Bots.ArenaProfilePreloadPatch.Reset();
        Bots.CorpseCleanupService.Reset();
        Performance.GcThresholdMonitor.Reset();
        Performance.PoolPrewarmMonitor.Reset();
        Diagnostics.ArenaDamageExceptionLog.Reset();
        Bots.CorpseLightService.Reset();
        Bots.CorpseWeaponRegistry.Reset();
        Bots.ArenaHostility.Reset();
        Bots.ArenaBotPatches.ResetRaidState();
        Bots.TeamArenaPursuit.Reset();
        Combat.ArenaGiveUp.Reset();
        Combat.WeaponSwapService.Reset();
        Kits.AutoQuickSlots.Reset();
        Kits.ArenaSecureContainer.Reset();
        Kits.ZombieOwnGearLoadout.Reset();
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
            game != null && game.Status == GameStatus.Started
                && Comfort.Common.Singleton<AbstractGame>.Instantiated
                && ReferenceEquals(Comfort.Common.Singleton<AbstractGame>.Instance, game)
                && Comfort.Common.Singleton<GameWorld>.Instantiated
                && ReferenceEquals(Comfort.Common.Singleton<GameWorld>.Instance, game.GameWorld)
                && ArenaSession.IsExactActiveRaid(game, game.GameWorld));

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
