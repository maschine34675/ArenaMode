using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Kits;
using ArenaMode.Session;
using EFT;
using Redeploy.Core.Engine;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Match lifecycle glue: creates exactly one ArenaMatch when an arena session
/// begins, ticks it from ArenaController, and tears it down at raid end.
/// The local death/respawn signals come from ArenaEvents (raised by the
/// respawn driver — they also cover deaths without an aggressor).
/// </summary>
internal static class ArenaMatchService
{
    public static ArenaMatch Current { get; private set; }
    public static bool TeamStartAbortPending
        => _teamStartAbortPending || _teamStartAbortCommitted;

    private static bool _initialized;
    private static bool _teamStartAbortPending;
    private static bool _teamStartAbortCommitted;
    private static string _teamStartAbortReason;
    private static float _nextScoreboardSyncAt;
    private static string _lastScoreboardProblem;

    public static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        RaidLifecycle.OnRaidStarted += world => StartMatch(world);
        RaidLifecycle.OnRaidEnded += OnRaidEnded;
        ArenaEvents.OnPlayerDied += OnLocalDied;
        ArenaEvents.OnPlayerRespawned += OnLocalRespawned;
    }

    public static void Tick()
    {
        if (_teamStartAbortPending)
            TryAbortFailedTeamStart();

        var match = Current;
        if (match == null)
            return;

        match.Tick(Time.deltaTime);

        if (ReferenceEquals(Current, match) && Time.unscaledTime >= _nextScoreboardSyncAt)
        {
            _nextScoreboardSyncAt = Time.unscaledTime + 0.25f;
            SynchronizeScoreboard(match);
        }
    }

    /// <summary>
    /// Called explicitly by ArenaSession before it swaps in the raid-start kit,
    /// and again by the lifecycle event as a backstop. Starting twice would tear
    /// the first match's HUD off its own match object, so the second call is a
    /// no-op.
    /// </summary>
    public static void StartMatch(GameWorld world)
    {
        if (Current != null || TeamStartAbortPending)
            return;

        // ArenaSession invokes this explicitly and the lifecycle dispatcher
        // invokes it again. A failed first attempt may already have committed
        // LocalGame.Stop while IsActive remains latched until GameEnd; never let
        // that second callback initialize a match inside the Stopping raid.
        if (!(Comfort.Common.Singleton<AbstractGame>.Instance is LocalGame game)
            || game.Status != GameStatus.Started)
            return;

        // ArenaSession's own OnRaidStarted handler decides IsActive; subscription
        // order between the two handlers is not guaranteed, so defer one frame
        // via the controller: cheapest is to check lazily here — IsActive is set
        // synchronously inside the same RaidLifecycle event dispatch, and
        // ArenaSession.Initialize() runs before ArenaMatchService.Initialize()
        // in Plugin.Awake, so its handler already ran when this one fires.
        if (!ArenaSession.IsActive)
            return;

        var protectedTeam = Compat.PitFireTeamCompat.TeamArenaActive;
        ArenaMatch partialMatch = null;
        try
        {
            if (protectedTeam
                && !ArenaServerClient.ValidateProtectedTeamRaid(out var validationFailure))
            {
                throw new InvalidOperationException(
                    "Protected 5v5 server validation failed: " + validationFailure);
            }

            if (!Effects.ArenaTracerTeams.BeginMatch(game.ProfileId, protectedTeam))
            {
                Plugin.ModLogger.LogWarning(
                    "Arena team tracers stayed unbound because the exact local profile ID was unavailable.");
            }

            Combat.CombatPatches.ResetRaidState();
            Audio.AnnouncerService.EnsureLoaded();
            // The server shaped its per-raid clone from the mode frozen during
            // LocalRaidStarted. Reading the mutable F12 setting here would let a
            // loading-screen change create a different client ruleset, including
            // a respawn mode for a protected PitFireTeam leader.
            var modeId = ArenaSession.ActiveMode;
            if (Compat.PitFireTeamCompat.TeamArenaActive
                && !string.Equals(modeId, "tactical", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.ModLogger.LogError(
                    $"Arena corrected protected PitFireTeam mode drift from '{modeId ?? "<null>"}' to Tactical.");
                modeId = "tactical";
            }

            var mode = ModeRegistry.Create(modeId);
            var local = world?.MainPlayer ?? game.LocalPlayer;
            var match = new ArenaMatch(
                mode,
                game.ProfileId,
                local?.Profile?.Nickname ?? game.Profile?.Nickname,
                Combat.RoleClassifier.Label(local));
            partialMatch = match;
            Current = match;
            _nextScoreboardSyncAt = 0f;
            _lastScoreboardProblem = null;
            // Subscribe presentation before mode startup. A mode is allowed to
            // raise its first synchronous cue from OnMatchStart; attaching
            // afterwards loses it.
            Audio.AnnouncerService.Attach(match);
            Audio.HitmarkerAudioService.Attach(match);
            Effects.ArenaLightingDirector.Attach(match, world);
            mode.OnMatchStart(match);
            SynchronizeScoreboard(match);
            Hud.ArenaHud.Attach(match);
            Plugin.ModLogger.LogInfo($"Arena match started ({mode.DisplayName}).");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError($"Arena match start failed: {ex}");
            Current = null;
            // A partially attached HUD would tick into the menu forever.
            Try(Hud.ArenaHud.Detach, "hud detach");
            Try(Audio.AnnouncerService.Detach, "announcer detach");
            Try(Audio.HitmarkerAudioService.Detach, "hitmarker audio detach");
            Try(Effects.ArenaLightingDirector.Reset, "lighting detach");
            Try(Effects.ArenaTeamLaserColors.Reset, "team laser detach");
            Effects.ArenaTracerTeams.Reset();
            Bots.TeamDeathmatchRelations.Reset();
            if (partialMatch != null)
                Try(partialMatch.End, "partial mode end");

            // With a frozen PitFireTeam roster, returning without a match would
            // leave a protected vanilla raid running forever: no Tactical Tick,
            // no staging guard and no result coordinator. End it immediately as
            // no contest, preserving the original leader body and both stores.
            // A failed solo Tactical startup must not fall through to the
            // generic raid-start random kit either: it has no buy coordinator.
            if (protectedTeam || Compat.PitFireTeamCompat.HasFrozenTeamRoster
                || string.Equals(ArenaSession.ActiveMode, Shared.ArenaPopulationPolicy.Tactical,
                    StringComparison.OrdinalIgnoreCase))
                ScheduleFailedStartAbort(ex.GetBaseException().Message);
        }
    }

    private static void OnRaidEnded()
    {
        Effects.ArenaTeamLaserColors.Reset();
        Effects.ArenaLightingDirector.Reset();
        Effects.ArenaTracerTeams.Reset();
        Bots.TeamDeathmatchRelations.Reset();
        var match = Current;
        Current = null;
        Try(Audio.HitmarkerAudioService.Detach, "hitmarker audio detach");
        _teamStartAbortPending = false;
        _teamStartAbortCommitted = false;
        _teamStartAbortReason = null;
        _nextScoreboardSyncAt = 0f;
        _lastScoreboardProblem = null;

        if (match == null)
            return;

        // Independent teardown steps — one failing must not skip the rest.
        Try(Hud.ArenaHud.Detach, "hud detach");
        Try(Audio.AnnouncerService.Detach, "announcer detach");
        Try(match.End, "mode end");
        Plugin.ModLogger.LogInfo(
            $"Arena match ended: {match.Kills} kills / {match.Deaths} deaths, "
            + $"score {match.Score}, best streak {match.BestStreak}, "
            + $"{match.Elapsed / 60f:0.0} min.");
    }

    /// <summary>
    /// A newly started exact GameWorld is a safe lifecycle boundary. If EFT
    /// skipped the preceding GameEnd callback, discard every match-owned latch
    /// before the new armed or ordinary raid can tick or receive damage.
    /// </summary>
    internal static void RecoverAtNewRaidStart()
    {
        // This boundary also runs for an ordinary raid after a missed GameEnd.
        // Clear cosmetic state even when no match latch survived.
        Try(Audio.HitmarkerAudioService.Detach, "hitmarker audio recovery");
        Effects.ArenaTeamLaserColors.Reset();
        Effects.ArenaLightingDirector.Reset();
        Effects.ArenaTracerTeams.Reset();
        Bots.TeamDeathmatchRelations.Reset();
        if (Current == null && !TeamStartAbortPending)
            return;

        Plugin.ModLogger.LogWarning(
            "Stale Arena match/abort state cleared at the next exact raid start.");
        OnRaidEnded();
    }

    internal static void ScheduleFailedStartAbort(string reason)
    {
        _teamStartAbortReason = string.IsNullOrWhiteSpace(reason)
            ? "Arena match initialization failed"
            : reason;
        if (!_teamStartAbortCommitted)
            _teamStartAbortPending = true;
        Respawn.ArenaRespawnDriver.StandDown = true;
        Compat.PitFireTeamCompat.BeginFinalizing();
        Plugin.ModLogger.LogError(
            $"Arena start is invalid and will end without a playable match: {_teamStartAbortReason}");
        TryAbortFailedTeamStart();
    }

    /// <summary>
    /// Consumes the one prepared body-replacement authorization owned by the
    /// active Tactical round. Keeping the mode cast here gives the respawn
    /// driver one fail-closed doorway and prevents another mode from opting a
    /// frozen PitFireTeam leader into Redeploy by accident.
    /// </summary>
    internal static bool TryTakeTeamRespawnAuthorization(
        LocalGame game,
        LocalPlayer deadPlayer,
        out TeamArenaPreparedLoadoutRestore prepared,
        out int teamEpoch,
        out string reason)
    {
        prepared = null;
        teamEpoch = 0;
        reason = null;

        if (Current?.Mode is not TacticalMode tactical)
        {
            reason = "the active Arena mode is not Tactical";
            return false;
        }

        try
        {
            return tactical.TryTakeTeamRespawnAuthorization(
                game,
                deadPlayer,
                out prepared,
                out teamEpoch,
                out reason);
        }
        catch (Exception ex)
        {
            prepared = null;
            teamEpoch = 0;
            reason = $"Tactical respawn authorization threw: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static void TryAbortFailedTeamStart()
    {
        if (!_teamStartAbortPending)
            return;

        // Refresh this on every retry, including frames where GameWorld has not
        // published MainPlayer yet. A slow/failed initialization must not let
        // PFT's bounded cleanup guard expire while the abort still owns the raid.
        Respawn.ArenaRespawnDriver.StandDown = true;
        Compat.PitFireTeamCompat.BeginFinalizing();

        LocalGame game = null;
        try
        {
            if (!(Comfort.Common.Singleton<AbstractGame>.Instance is LocalGame localGame))
            {
                return;
            }

            game = localGame;
            if (localGame.Status != GameStatus.Started)
                return;

            var mainPlayer = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer
                : null;
            // BaseLocalGame owns the immutable raid Profile even before
            // GameWorld.MainPlayer is published. It is the authoritative Stop
            // identity and prevents a failed Tactical bind from soft-locking.
            var profileId = mainPlayer?.ProfileId?.Trim();
            if (string.IsNullOrEmpty(profileId))
                profileId = localGame.ProfileId?.Trim();
            if (string.IsNullOrEmpty(profileId))
                return;

            var body = mainPlayer ?? localGame.LocalPlayer;
            var exitStatus = body?.HealthController?.IsAlive == true
                ? ExitStatus.MissingInAction
                : ExitStatus.Killed;

            game.Stop(profileId, exitStatus, "Arena", 0f);
            _teamStartAbortPending = false;
            _teamStartAbortCommitted = true;
            Plugin.ModLogger.LogWarning(
                $"Arena invalid start ended without a playable match ({exitStatus}): {_teamStartAbortReason}");
        }
        catch (Exception ex)
        {
            // Stop changes the native game status before optional teardown. A
            // post-commit exception must not trigger a second Stop. Before that
            // point, restore the live guards and retry on the next Tick.
            var committed = game != null && game.Status != GameStatus.Started;
            if (committed)
            {
                _teamStartAbortPending = false;
                _teamStartAbortCommitted = true;
            }
            else
            {
                // Keep every guard active. The next Tick refreshes finalization
                // and retries the same stable LocalGame profile identity.
                Respawn.ArenaRespawnDriver.StandDown = true;
                Compat.PitFireTeamCompat.BeginFinalizing();
            }

            Plugin.ModLogger.LogError(
                committed
                    ? $"Arena invalid-start stop failed after EFT committed teardown; protection remains active: {ex}"
                    : $"Arena invalid-start stop failed before commit and will retry: {ex}");
        }
    }

    private static void Try(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena match {name} failed: {ex}");
        }
    }

    /// <summary>
    /// Copies the current physical roster into plain scoreboard data. Tactical
    /// owns its exact side/account identities; every other Arena mode is a
    /// player-versus-field board where each generated profile is one bot life.
    /// </summary>
    private static void SynchronizeScoreboard(ArenaMatch match)
    {
        if (ArenaMatchResult.HasResult(match?.Mode)) return;
        try
        {
            if (match?.Mode is TeamDeathmatchMode teams)
            {
                if (!teams.TrySynchronizeScoreboardRoster(out var teamProblem))
                    NoteScoreboardProblem(teamProblem);
                else
                    _lastScoreboardProblem = null;
                return;
            }
            if (match?.Mode is TacticalMode tactical)
            {
                if (!tactical.TrySynchronizeScoreboardRoster(out var tacticalProblem))
                    NoteScoreboardProblem(tacticalProblem);
                else
                    _lastScoreboardProblem = null;
                return;
            }
            // Zombie owns its entire population and has no opposing roster to
            // show (every bot is hostile only to the player) — the same reason
            // TDM/Tactical return early above, just without a per-mode roster
            // of their own to build instead. Skipping this avoids a per-bot
            // actor allocation every 0.25s for a board nothing reads.
            if (match?.Mode is ZombieMode)
                return;

            var world = Comfort.Common.Singleton<GameWorld>.Instantiated
                ? Comfort.Common.Singleton<GameWorld>.Instance
                : null;
            var localPlayer = world?.MainPlayer;
            if (ReferenceEquals(world, null)
                || world == null
                || ReferenceEquals(localPlayer, null)
                || localPlayer == null)
            {
                NoteScoreboardProblem("the current GameWorld or local player is unavailable");
                return;
            }

            ArenaScoreboardActor local = SnapshotActor(localPlayer);
            var opponents = new List<ArenaScoreboardActor>(world.RegisteredPlayers.Count);
            foreach (IPlayer player in world.RegisteredPlayers)
            {
                if (ReferenceEquals(player, null)
                    || ReferenceEquals(player, localPlayer)
                    || player.IsYourPlayer
                    || string.Equals(player.ProfileId, localPlayer.ProfileId, StringComparison.Ordinal))
                {
                    continue;
                }

                ArenaScoreboardActor actor = SnapshotActor(player);
                if (actor != null)
                    opponents.Add(actor);
            }

            int epoch = match.Mode is RoundsMode rounds ? rounds.Round.Number : 1;
            if (!match.Scoreboard.TrySynchronizeRoster(
                    epoch,
                    local,
                    Array.Empty<ArenaScoreboardActor>(),
                    opponents,
                    out var problem))
            {
                NoteScoreboardProblem(problem);
                return;
            }

            _lastScoreboardProblem = null;
        }
        catch (Exception ex)
        {
            NoteScoreboardProblem(ex.GetBaseException().Message);
        }
    }

    internal static ArenaScoreboardActor SnapshotActor(IPlayer player, string stableId = null)
    {
        if (ReferenceEquals(player, null) || string.IsNullOrWhiteSpace(player.ProfileId))
            return null;

        return new ArenaScoreboardActor(
            player.ProfileId,
            Combat.RoleClassifier.DisplayName(player),
            Combat.RoleClassifier.Label(player),
            player.HealthController?.IsAlive == true,
            stableId);
    }

    private static void NoteScoreboardProblem(string problem)
    {
        if (string.IsNullOrWhiteSpace(problem)
            || string.Equals(problem, _lastScoreboardProblem, StringComparison.Ordinal))
        {
            return;
        }

        _lastScoreboardProblem = problem;
        Plugin.ModLogger.LogWarning("Arena scoreboard roster stayed on its last valid snapshot: " + problem);
    }

    private static void OnLocalDied(Player player)
        => Current?.ReportLocalDeath();

    private static void OnLocalRespawned(Player player)
        => Current?.ReportLocalRespawn(player);
}
