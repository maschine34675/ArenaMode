using ArenaMode.Config;
using ArenaMode.Events;
using ArenaMode.Kits;
using ArenaMode.Session;
using EFT;
using Redeploy.Core.Engine;
using System;
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
            var match = new ArenaMatch(mode);
            partialMatch = match;
            Current = match;
            // Subscribe presentation before mode startup. A mode is allowed to
            // raise its first synchronous cue from OnMatchStart; attaching
            // afterwards loses it.
            Audio.AnnouncerService.Attach(match);
            Effects.ArenaLightingDirector.Attach(match, world);
            mode.OnMatchStart(match);
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
            Try(Effects.ArenaLightingDirector.Reset, "lighting detach");
            Effects.ArenaTracerTeams.Reset();
            if (partialMatch != null)
                Try(partialMatch.End, "partial mode end");

            // With a frozen PitFireTeam roster, returning without a match would
            // leave a protected vanilla raid running forever: no Tactical Tick,
            // no staging guard and no result coordinator. End it immediately as
            // no contest, preserving the original leader body and both stores.
            if (protectedTeam || Compat.PitFireTeamCompat.HasFrozenTeamRoster)
                ScheduleFailedStartAbort(ex.GetBaseException().Message);
        }
    }

    private static void OnRaidEnded()
    {
        Effects.ArenaLightingDirector.Reset();
        Effects.ArenaTracerTeams.Reset();
        var match = Current;
        Current = null;
        _teamStartAbortPending = false;
        _teamStartAbortCommitted = false;
        _teamStartAbortReason = null;

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
        Effects.ArenaLightingDirector.Reset();
        Effects.ArenaTracerTeams.Reset();
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

    private static void OnLocalDied(Player player)
        => Current?.ReportLocalDeath();

    private static void OnLocalRespawned(Player player)
        => Current?.ReportLocalRespawn(player);
}
