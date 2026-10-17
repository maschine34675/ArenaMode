using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

public sealed partial class TeamDeathmatchMode
{
    private TeamDeathmatchMatchPolicy _rules;
    private bool _resultPublished, _localDeathCounted, _resultExitRequested;
    private float _resultDisplayAt = -1f, _nextStopAttempt;
    private string _resultExitProblem;
    private float _gateCountdownEndsAt;

    internal bool HasResult => _rules?.Phase == TdmMatchPhase.Finished;
    // Read-only: AI admission must never advance clocks or publish a result.
    internal bool IsSearchActive => IsCurrentMatch() && _rules?.Phase == TdmMatchPhase.Live;
    internal bool ResultOwnsCurrentRaid => HasResult && IsCurrentRaid()
        && ReferenceEquals(ArenaMatchService.Current, _match);
    internal float PlayingElapsed => _rules?.Elapsed(Time.time) ?? 0f;
    internal bool TryGetDifficultyClock(out float elapsed, out int duration)
    {
        elapsed = 0; duration = 0;
        if (!IsCurrentMatch() || _rules == null) return false;
        elapsed = PlayingElapsed;
        duration = _rules.TimeLimitSeconds;
        return true;
    }
    internal string ResultHeading => _rules?.Result == TdmMatchResult.Won ? "VICTORY"
        : _rules?.Result == TdmMatchResult.Lost ? "DEFEAT" : "DRAW";
    internal string ResultDetail => $"{FriendlyFaction} {_rules?.FriendlyKills} : {_rules?.OpponentKills} {OpponentFaction} — {_rules?.Reason}";
    internal string ResultExitStatus => _resultExitProblem ?? (TdmMatchExit.PipelineRunning
        ? "Finishing player replacement…" : $"Returning to menu in {ResultDisplayRemaining}s");
    internal int ResultDisplayRemaining => _resultDisplayAt < 0f ? (int)TeamDeathmatchMatchPolicy.ResultSeconds
        : Math.Max(0, (int)Math.Ceiling(TeamDeathmatchMatchPolicy.ResultSeconds - (Time.unscaledTime - _resultDisplayAt)));
    private string RulesStatus => _rules?.Phase == TdmMatchPhase.Assembling ? "ASSEMBLING TEAMS"
        : _rules?.Phase == TdmMatchPhase.Countdown ? "START IN " + _rules.CountdownRemaining(Time.time)
        : (_rules?.KillLimit > 0 ? "First to " + _rules.KillLimit : "No kill limit")
          + (_rules?.TimeLimitSeconds > 0 ? $" | {Math.Max(0, (int)Math.Ceiling(_rules.TimeLimitSeconds - PlayingElapsed)) / 60:00}:{Math.Max(0, (int)Math.Ceiling(_rules.TimeLimitSeconds - PlayingElapsed)) % 60:00} left" : " | No time limit");

    private void TickMatchStart(bool ready)
    {
        if (_rules == null || _announcedReady) return;
        var before = _rules.Phase;
        if (ready && before == TdmMatchPhase.Countdown && Time.time >= _gateCountdownEndsAt
            && !Bots.ArenaEquatorTeamSpawns.TryOpenForCombat()) return;
        _rules.Update(Time.time, ready, 0, 0);
        if (before != TdmMatchPhase.Countdown && _rules.Phase == TdmMatchPhase.Countdown)
            _gateCountdownEndsAt = Time.time + TeamDeathmatchMatchPolicy.CountdownSeconds;
        if (before != _rules.Phase)
            Plugin.ModLogger.LogDebug($"Team Deathmatch phase: {before} -> {_rules.Phase}; "
                + $"killLimit={_rules.KillLimit}, timeLimit={_rules.TimeLimitSeconds}s.");
    }

    // Called before a score-bearing event as well as after its bookkeeping.
    // This makes a hit at/after the deadline unable to change an expired result.
    internal bool TryBeginScoredEvent()
    {
        if (!IsCurrentMatch()) return false;
        CheckMatchLimits();
        return !IsComplete && _rules?.Phase == TdmMatchPhase.Live;
    }

    internal bool TryRecordLocalDeath()
    {
        if (_localDeathCounted || !TryBeginScoredEvent()) return false;
        _localDeathCounted = true;
        return true;
    }

    private void CheckMatchLimits()
    {
        if (_rules?.Phase != TdmMatchPhase.Live || !IsCurrentMatch()) return;
        var friendly = _match.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Kills;
        var opponent = _match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills;
        _rules.Update(Time.time, false, friendly, opponent);
        if (!HasResult || _resultPublished) return;
        _resultPublished = true;
        _population.Stop();
        _pendingGiveUpLife = null;
        // Keep the death driver registered through the final-hit callbacks.
        // Its terminal hold blocks new engine entries; a started replacement
        // must unwind before the native raid owner can be safely stopped.
        Plugin.ModLogger.LogInfo($"Team Deathmatch result: {_rules.Result}; {_rules.Reason}; "
            + $"{FriendlyFaction} {friendly}:{opponent} {OpponentFaction}; played={PlayingElapsed:F1}s.");
        ArenaToasts.Show(ResultHeading + " — " + ResultDetail);
        if (_rules.Result == TdmMatchResult.Won) _match.ReportAnnouncerCue(ArenaAnnouncerCue.MatchWon);
        else if (_rules.Result == TdmMatchResult.Lost) _match.ReportAnnouncerCue(ArenaAnnouncerCue.MatchLost);
    }

    /// <summary>Uses the same captured world even after IsComplete turns true.</summary>
    internal bool ShouldBlockMatchDamage(Player victim)
    {
        if (victim == null || !IsCurrentRaid() || !ReferenceEquals(ArenaMatchService.Current, _match)
            || (_rules?.Phase == TdmMatchPhase.Live && !HasResult)) return false;
        if (ReferenceEquals(victim, _world.MainPlayer) && victim.ProfileId == _localId) return true;
        var owner = victim.AIData?.BotOwner;
        return owner != null && ReferenceEquals(owner.GetPlayer, victim)
            && _bots.Exists(bot => ReferenceEquals(bot, owner));
    }

    internal bool TryGetMatchNotice(out string heading, out string detail)
    {
        heading = detail = null;
        if (!IsCurrentRaid() || !ReferenceEquals(ArenaMatchService.Current, _match)) return false;
        if (HasResult)
        {
            heading = ResultHeading;
            detail = ResultDetail + " | " + ResultExitStatus;
            return true;
        }
        if (_rules?.Phase == TdmMatchPhase.Live || _rules == null) return false;
        heading = Bots.ArenaEquatorTeamSpawns.GatesOpening ? "OPENING SPAWN GATES"
            : _rules.Phase == TdmMatchPhase.Countdown
            ? "MATCH STARTS IN " + _rules.CountdownRemaining(Time.time) : "ASSEMBLING TEAMS";
        detail = $"{FriendlyFaction}: {CountBots(_population.LocalUsec) + (_local?.HealthController?.IsAlive == true ? 1 : 0)}/{TeamSize}"
            + $"  |  {OpponentFaction}: {CountBots(!_population.LocalUsec)}/{TeamSize}  |  Damage and scoring disabled";
        return true;
    }

    internal void RequestResultExit()
    {
        if (ResultOwnsCurrentRaid) _resultExitRequested = true;
    }

    internal void ReportResultExitProblem(string problem)
    {
        if (!ResultOwnsCurrentRaid || string.Equals(problem, _resultExitProblem, StringComparison.Ordinal)) return;
        _resultExitProblem = problem;
        Plugin.ModLogger.LogWarning("Team Deathmatch result: " + problem);
    }

    private void TickResult()
    {
        if (!ResultOwnsCurrentRaid) return;
        if (TdmMatchExit.PipelineRunning) return;
        if (_resultDisplayAt < 0f) _resultDisplayAt = Time.unscaledTime;
        if (!_resultExitRequested && ResultDisplayRemaining > 0) return;
        if (Time.unscaledTime < _nextStopAttempt) return;
        _nextStopAttempt = Time.unscaledTime + 5f;
        TdmMatchExit.TryStop(_game, _world, _localId, ReportResultExitProblem);
    }
}
