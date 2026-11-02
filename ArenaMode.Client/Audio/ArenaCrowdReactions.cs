using System;
using System.Collections.Generic;
using System.Globalization;
using ArenaMode.Combat;
using ArenaMode.Compat;
using ArenaMode.Events;
using ArenaMode.Modes;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using UnityEngine;
using static ArenaMode.Compat.ArenaMapPortSpectatorCrowd;

namespace ArenaMode.Audio;

/// <summary>
/// The spectator crowd of ArenaMapPort maps follows the Arena match: reactions (cheer, anger,
/// round start and end) through MapPort's optional reaction claim, and the murmur's sustained
/// excitement through SpectatorCrowd.SetExcitement. Presentation only: no Harmony patch, no
/// match state, no profile. It lives entirely on ArenaMatch and ArenaEvents subscriptions inside
/// an attached Arena match, so an ordinary raid never reaches it. Contract, decisions and
/// mapping: docs/reviews/CROWD-REACTIONS-20261102.md; the MapPort side is
/// D:\ArenaMapPort\docs\PLAN-CROWD-REACTIONS.md (phase A there, this is phase B).
///
/// Lifecycle. ArenaMatchService attaches it with the announcer, hitmarker and lighting, before
/// mode.OnMatchStart (a mode may raise its first cue there), and detaches it on the failed start,
/// at raid end, at new-raid recovery and at plugin shutdown. Native Stop and the failed-start abort
/// make ArenaMatchService.Tick return early; both branches call <see cref="OnNativePresentationStop"/>,
/// as TeamDeathmatchMode does for the score monitors. Independently, every <see cref="Tick"/>
/// applies the announcement director's push gate (this exact match is current, the game is
/// Started, the raid is the exact active Arena raid) and also refuses a pending start abort; a
/// failed gate releases everything for the rest of the match. A claim never outlives any of them.
///
/// Tick source. ArenaController.Update calls <see cref="Tick"/> right after AnnouncerService.Tick,
/// every frame, whether the local player is alive, dead, in a death replay or in the team
/// spectator camera: reactions while dead are allowed (decision D12; MapPort keeps its own
/// living-player guard for the random calls only). After the announcer, because its director emits
/// this frame's lead, draw and ladder cues, which must land in the same flush as the kill or round
/// event that caused them.
///
/// Events are buffered per frame and flushed once from the tick: per kind the strongest entry,
/// a Start or Finish drops that frame's Approve and Disapprove, at most one push per kind per
/// flush, nothing when nothing happened. Handlers never log and never throw into the match.
/// </summary>
internal static class ArenaCrowdReactions
{
    // Plan 6.2: retry only ReactionKinds/ClaimReactions, until Status == Started plus 30 s, at most
    // once per second. The crowd is built during raid loading, so the first attempt normally wins;
    // the window covers a MapPort that publishes its reaction set late (worker load, audio reset).
    internal const float ClaimWindowSeconds = 30f;
    internal const float ClaimRetrySeconds = 1f;
    // The snapshot changes on the scale of seconds and MapPort smooths the level with a 2 s time
    // constant; polling faster would only add calls.
    internal const float ExcitementPollSeconds = 1f;
    // MapPort deduplicates equal owner strings and truncates them at 40 characters (as for the
    // score monitors, TeamDeathmatchMode.Monitors). "AM crowd " plus 31 hex digits of a fresh
    // Guid is exactly 40: unique per match, never shortened into another match's owner.
    internal const string OwnerPrefix = "AM crowd ";
    private const int OwnerHexDigits = 31;

    // TakeCover is MapPort's own sensor (plan 4.2); Lull and PurifiedTeam are reserved.
    private static readonly int WantedKinds = Bit(Approve) | Bit(Disapprove) | Bit(Start) | Bit(Finish);
    private static readonly ArenaCrowdReactionPolicy.Frame Pending = new();
    // Bodies whose local death already owns its reaction (see TakeDeath).
    private static readonly HashSet<long> HandledDeaths = new();

    private static ArenaMatch _match;
    private static RoundsMode _rounds;
    private static Contract _contract;
    private static ReactionClaim _claim;
    private static string _owner;
    private static bool _listening, _claimClosed, _silenced, _finishArmed, _windowStarted;
    private static bool _excitementOff, _handlerFailed;
    private static float _windowEndsAt, _nextClaimAt, _nextExcitementAt;
    private static int _excitementStep, _excitementPeak, _sent, _claimAttempts, _lastKinds;

    internal static void Attach(ArenaMatch match) => Attach(match, ArenaMapPortSpectatorCrowd.Bind);

    // The bind is a parameter only for production-linked checks (tools/CrowdReactionChecks), which
    // hand in BindContract over stand-in MapPort surfaces; production always binds the loaded plugin.
    internal static void Attach(ArenaMatch match, Func<Contract> bind)
    {
        Detach();
        if (match == null) return;
        try
        {
            // One F12 key for reactions and excitement (decision D10), frozen at arm like the
            // announcer's per-mode switch: a mid-raid F12 edit must not start or stop the crowd.
            if (!ArenaSession.CrowdReactionsSelection)
            {
                Plugin.ModLogger.LogDebug("Arena crowd off: Audio/CrowdReactions was off when the raid was armed.");
                return;
            }
            // Decision D5: no reactions and no excitement in Zombies v1, not even a claim. Its
            // endless waves have no round result, its constant melee would keep the crowd busy,
            // and the snapshot has no wave state to drive the murmur.
            if (match.Mode is ZombieMode)
            {
                Plugin.ModLogger.LogDebug("Arena crowd off: Zombies has no crowd reactions or murmur intensity.");
                return;
            }
            var contract = bind?.Invoke();
            if (contract == null || (contract.Excitement != Part.Bound && contract.Reactions != Part.Bound))
            {
                Plugin.ModLogger.LogDebug("Arena crowd off: " + (contract?.Summary ?? "no contract") + ".");
                return;
            }
            _match = match;
            _contract = contract;
            _finishArmed = true;
            if (contract.Reactions == Part.Bound)
            {
                _owner = OwnerPrefix + Guid.NewGuid().ToString("N").Substring(0, OwnerHexDigits);
                _listening = true;
                match.OnKill += OnKill;
                match.OnCallout += OnCallout;
                match.OnAnnouncerCue += OnAnnouncerCue;
                ArenaEvents.OnPlayerDied += OnPlayerDied;
                if (match.Mode is RoundsMode rounds)
                {
                    _rounds = rounds;
                    rounds.OnRoundStarted += OnRoundStarted;
                    rounds.OnRoundEnded += OnRoundEnded;
                }
            }
            else _claimClosed = true;
            Plugin.ModLogger.LogDebug($"Arena crowd attached: mode={match.Mode?.Id}, {contract.Summary}.");
        }
        catch (Exception ex)
        {
            Detach();
            Plugin.ModLogger.LogWarning("Arena crowd presentation unavailable: " + ex.GetBaseException().Message);
        }
    }

    internal static void Detach() => Teardown("detach");

    /// <summary>
    /// Called by ArenaMatchService.Tick on its two early returns (native Stop owns the raid, a
    /// failed-start abort is pending), where no mode tick runs any more. Cheap when detached.
    /// </summary>
    internal static void OnNativePresentationStop() => Teardown("native stop");

    internal static void Tick()
    {
        var match = _match;
        if (match == null) return;
        try
        {
            var game = Singleton<AbstractGame>.Instance as LocalGame;
            var world = Singleton<GameWorld>.Instance;
            if (!ReferenceEquals(match, ArenaMatchService.Current) || ArenaMatchService.TeamStartAbortPending
                || game == null || game.Status != GameStatus.Started
                || !ArenaSession.IsExactActiveRaid(game, world))
            {
                // None of these comes back for the same match: a replaced or ended match, an
                // abort, Stopping/Stopped, another raid. Release now instead of waiting for GameEnd.
                Teardown("match or raid no longer current");
                return;
            }
            if (_silenced) return;
            float now = Time.unscaledTime;
            if (!_windowStarted)
            {
                _windowStarted = true;
                _windowEndsAt = now + ClaimWindowSeconds;
                _nextClaimAt = _nextExcitementAt = now;
            }
            TickClaim(now);
            Flush();
            TickExcitement(match, now);
        }
        catch (Exception ex)
        {
            Teardown("failure");
            Plugin.ModLogger.LogWarning("Arena crowd presentation stopped: " + ex.GetBaseException().Message);
        }
    }

    private static void TickClaim(float now)
    {
        if (_claim != null || _claimClosed) return;
        if (now > _windowEndsAt)
        {
            _claimClosed = true;
            // ReactionKinds without our kinds after the window: no crowd (Iceberg, a vanilla map,
            // no reaction set, reactions switched off in MapPort). Normal, so Debug.
            Plugin.ModLogger.LogDebug((_lastKinds & WantedKinds) == 0
                ? $"Arena crowd reactions not claimed: no reaction kinds on this map ({_claimAttempts} checks)."
                : $"Arena crowd reactions not claimed: claim refused ({_claimAttempts} attempts, kinds=0x{_lastKinds:X}).");
            return;
        }
        if (now < _nextClaimAt) return;
        _nextClaimAt = now + ClaimRetrySeconds;
        _claimAttempts++;
        var claim = ReactionClaim.TryClaim(_contract, _owner, WantedKinds, out int kinds, out bool failed);
        _lastKinds = kinds;
        if (failed) { _claimClosed = true; return; }
        if (claim == null) return;
        _claim = claim;
        Plugin.ModLogger.LogDebug($"Arena crowd reactions claimed: handle={claim.Handle}, kinds=0x{kinds:X}, "
            + $"attempt={_claimAttempts}.");
    }

    private static void Flush()
    {
        if (Pending.IsEmpty) return;
        var claim = _claim;
        if (claim == null) { Pending.Clear(); return; }
        Pending.Resolve();
        // Read once per flush, after the announcer handled this frame's callouts and cues.
        bool announcer = AnnouncerService.IsPmcVoiceBlocked;
        foreach (int kind in ArenaCrowdReactionPolicy.Frame.Order)
        {
            if (!Pending.TryTake(kind, out float strength, out string source) || !claim.Has(kind)) continue;
            bool weakened = announcer && ArenaCrowdReactionPolicy.IsInterlocked(kind);
            if (weakened) strength *= ArenaCrowdReactionPolicy.InterlockFactor;
            if (!claim.Push(kind, strength))
            {
                // A stale, foreign or refused claim: Push released it. Not claimed again in this
                // match; the murmur's excitement is independent and keeps running.
                _claim = null;
                _claimClosed = true;
                Pending.Clear();
                Plugin.ModLogger.LogDebug($"Arena crowd reactions released: handle={claim.Handle}, "
                    + $"reason=reaction refused, sent={_sent}.");
                return;
            }
            _sent++;
            Plugin.ModLogger.LogDebug($"Arena crowd reaction: kind={KindName(kind)}, strength={Number(strength)}, "
                + $"source={source}{(weakened ? ", announcer speaking" : "")}.");
        }
        Pending.Clear();
    }

    private static void TickExcitement(ArenaMatch match, float now)
    {
        var contract = _contract;
        if (_excitementOff || contract?.SetExcitement == null || now < _nextExcitementAt) return;
        _nextExcitementAt = now + ExcitementPollSeconds;
        try
        {
            int step = ArenaCrowdReactionPolicy.ExcitementStep(ArenaAnnouncementDirector.ReadSnapshot(match.Mode));
            if (step == _excitementStep) return;
            // No bed (a vanilla map, Iceberg, the bed switched off): MapPort calls the write harmless,
            // but the level is process-global and only a crowd removal resets it, which a map without
            // a crowd never has. Skipping keeps it from lingering; the step is written once it can be.
            if (contract.IsAvailable != null && !contract.IsAvailable()) return;
            contract.SetExcitement(step / (float)ArenaCrowdReactionPolicy.ExcitementSteps);
            _excitementStep = step;
            if (step > _excitementPeak) _excitementPeak = step;
        }
        catch (Exception ex)
        {
            _excitementOff = true;
            ResetExcitement();
            Plugin.ModLogger.LogWarning("Arena crowd murmur intensity stopped: " + ex.GetBaseException().Message);
        }
    }

    private static bool Listening => _listening && !_silenced;

    private static void OnKill(ArenaKill kill)
    {
        if (!Listening || kill == null) return;
        try
        {
            if (kill.LocalIsVictim && !TakeDeath(kill.VictimBodyId)) return;
            if (ArenaCrowdReactionPolicy.TryMapKill(kill, out int kind, out float strength, out string source))
                Pending.Add(kind, strength, source);
        }
        catch (Exception ex) { HandlerFailed(ex); }
    }

    // Deaths without a kill record (environment, falls, own grenade, an unscored aggressor) reach
    // the crowd only here. A death with a kill record arrives on both paths: EFT's Player.OnDead
    // runs OnBeenKilledByAggressor (kill capture, ArenaMatch.OnKill) before the DiedEvent's later
    // CG_Spawn handler, where Redeploy's driver raises ArenaEvents.OnPlayerDied. Both carry the
    // same physical body: ArenaKill.VictimBodyId is ArenaHitHistory.BodyId of that body. Asking
    // for it here only creates the weak identity entry the kill capture would create for the same
    // object; it records no contact and changes no award or scoring state.
    private static void OnPlayerDied(Player player)
    {
        if (!Listening) return;
        try
        {
            if (TakeDeath(ArenaHitHistory.BodyId(player)))
                Pending.Add(Disapprove, ArenaCrowdReactionPolicy.DeathStrength, "death");
        }
        catch (Exception ex) { HandlerFailed(ex); }
    }

    /// <summary>
    /// One local life, one death decision, whichever notification comes first and whatever frame
    /// the second arrives in: keyed by the physical body, not by timing. The first notification
    /// decides (an unscored kill record decides "no reaction"). An unknown body (id 0) cannot be
    /// deduplicated; both notifications then land in one frame and the flush keeps one Disapprove.
    /// </summary>
    private static bool TakeDeath(long bodyId) => bodyId == 0 || HandledDeaths.Add(bodyId);

    private static void OnCallout(string label)
    {
        if (!Listening) return;
        try
        {
            if (ArenaCrowdReactionPolicy.TryMapCallout(label, out float strength))
                Pending.Add(Approve, strength, label);
        }
        catch (Exception ex) { HandlerFailed(ex); }
    }

    private static void OnAnnouncerCue(ArenaAnnouncerCue cue)
    {
        if (!Listening) return;
        try
        {
            if (cue == ArenaAnnouncerCue.NoContest)
            {
                // A technical teardown (failed start, abort), once per match. No reaction; like the
                // announcer, which latches it until detach, the crowd stays silent for the rest of
                // the match: the claim goes back and the murmur calms.
                _silenced = true;
                StopPresenting("no contest");
                return;
            }
            if (!ArenaCrowdReactionPolicy.TryMapCue(cue, out int kind, out float strength)) return;
            if (!ArmFinish(kind)) return;
            Pending.Add(kind, strength, cue.ToString());
        }
        catch (Exception ex) { HandlerFailed(ex); }
    }

    /// <summary>
    /// At most one Finish per round, re-armed by its Start. Deathmatch reports a non-terminal
    /// draw twice (OnRoundEnded, then the director's Draw cue at its next tick); the latch keeps
    /// that to one reaction without depending on both landing in the same frame.
    /// </summary>
    private static bool ArmFinish(int kind)
    {
        if (kind == Start) _finishArmed = true;
        else if (kind == Finish)
        {
            if (!_finishArmed) return false;
            _finishArmed = false;
        }
        return true;
    }

    private static void OnRoundStarted(int round)
    {
        if (!Listening) return;
        // Deathmatch has no RoundStart cue; this event also fires for round 1.
        if (ArmFinish(Start)) Pending.Add(Start, ArenaCrowdReactionPolicy.RoundStrength, "deathmatch round start");
    }

    private static void OnRoundEnded(RoundsMode.RoundState state)
    {
        if (!Listening) return;
        try
        {
            // RoundsMode.PublishRoundEnd raises the match result cue first, then this event, in the
            // same call. A terminal round end has therefore already reacted (MatchWon, MatchLost),
            // silenced the match (NoContest) or is a terminal draw, whose Draw cue the director
            // raises at its next tick. Only a round that leaves the match running sends its own Finish.
            var rounds = _rounds;
            if (rounds == null || rounds.IsComplete) return;
            if (ArmFinish(Finish)) Pending.Add(Finish, ArenaCrowdReactionPolicy.RoundStrength, "deathmatch round end");
        }
        catch (Exception ex) { HandlerFailed(ex); }
    }

    private static void HandlerFailed(Exception ex)
    {
        if (_handlerFailed) return;
        _handlerFailed = true;
        Plugin.ModLogger.LogWarning("Arena crowd reaction event failed: " + ex.GetBaseException().Message);
    }

    // Release the claim and calm the murmur; the match may still be attached (no contest).
    private static void StopPresenting(string reason)
    {
        var claim = _claim;
        _claim = null;
        _claimClosed = true;
        Pending.Clear();
        if (claim != null && claim.Release())
            Plugin.ModLogger.LogDebug($"Arena crowd reactions released: handle={claim.Handle}, reason={reason}, "
                + $"sent={_sent}.");
        ResetExcitement();
    }

    // Plan 6.4: set 0 on release when a non-zero level was sent. MapPort resets the level when the
    // raid's crowd is removed, but a match can end long before that (result hold, menu return).
    private static void ResetExcitement()
    {
        int step = _excitementStep;
        _excitementStep = 0;
        var excite = _contract?.SetExcitement;
        if (step == 0 || excite == null) return;
        try { excite(0f); }
        catch (Exception ex)
        { Plugin.ModLogger.LogWarning("Arena crowd murmur intensity reset failed: " + ex.GetBaseException().Message); }
    }

    private static void Teardown(string reason)
    {
        var match = _match;
        if (match == null) return;
        if (_listening)
        {
            match.OnKill -= OnKill;
            match.OnCallout -= OnCallout;
            match.OnAnnouncerCue -= OnAnnouncerCue;
            ArenaEvents.OnPlayerDied -= OnPlayerDied;
        }
        var rounds = _rounds;
        if (rounds != null)
        {
            rounds.OnRoundStarted -= OnRoundStarted;
            rounds.OnRoundEnded -= OnRoundEnded;
        }
        StopPresenting(reason);
        Plugin.ModLogger.LogDebug($"Arena crowd detached: reason={reason}, reactions={_sent}, "
            + $"excitementPeak={Number(_excitementPeak / (float)ArenaCrowdReactionPolicy.ExcitementSteps)}.");
        _match = null;
        _rounds = null;
        _contract = null;
        _owner = null;
        _listening = _claimClosed = _silenced = _finishArmed = _windowStarted = false;
        _excitementOff = _handlerFailed = false;
        _windowEndsAt = _nextClaimAt = _nextExcitementAt = 0f;
        _excitementStep = _excitementPeak = _sent = _claimAttempts = _lastKinds = 0;
        HandledDeaths.Clear();
        Pending.Clear();
    }

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
