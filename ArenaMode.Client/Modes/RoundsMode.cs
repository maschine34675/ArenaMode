using ArenaMode.Combat;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Deathmatch scoring brackets inside one raid. The established RoundsMode type
/// and round events remain intact for HUD/lighting consumers; accepted options
/// are immutable for this match and the native world is never rebuilt by rules.
/// </summary>
public sealed partial class RoundsMode : IArenaMode
{
    public sealed class RoundState
    {
        public int Number = 1;
        public int Kills;
        public int Deaths;
        public int Score;
        public float StartedAt;
        public string WinnerProfileId;
        public string WinnerName;
        public string EndReason;
        public bool IsDraw;
    }

    private const float StuckRespawnGraceSeconds = 30f;
    private readonly DeathmatchOptions _options;
    private ArenaMatch _match;
    private DeathmatchRules _rules;
    private LocalGame _game;
    private GameWorld _world;
    private string _localId;
    private bool _ended, _localDeathCounted, _breakRespawnPending, _resultExitRequested;
    private int _scoreAtRoundStart, _publishedRound, _breakGeneration;
    private float _intermissionEndsAt, _resultDisplayAt = -1f, _nextStopAttempt;
    private string _resultExitProblem;

    public RoundsMode(DeathmatchOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    public string Id => "deathmatch";
    public string DisplayName => "Deathmatch";
    public bool IsComplete => HasResult;
    public DeathmatchOptions Options => _options;
    public RoundState Round { get; } = new RoundState();
    public bool InIntermission => !_ended && _rules?.Phase == DeathmatchPhase.Intermission;
    internal bool AssistScoringOpen => !_ended && IsCurrentRaid() && _rules?.Phase == DeathmatchPhase.Live;
    public bool IntermissionProtected => InIntermission && IsCurrentRaid() && ArenaProtection.IsProtectedNow;
    public float IntermissionRemaining => InIntermission ? Mathf.Max(0f, _intermissionEndsAt - Time.time) : 0f;
    public float TimeRemaining => (float)(_rules?.TimeRemaining(Time.time) ?? -1d);
    public int KillTarget => _options.Kills;
    public int RoundLimit => _options.Rounds;
    public int RoundsWon => _rules?.LocalRoundWins ?? 0;
    public int RoundsLost => _rules?.LocalRoundLosses ?? 0;
    public bool FreeForAll => _options.FreeForAll;
    public bool Continuous => _options.Continuous;
    public event Action<RoundState> OnRoundEnded;
    public event Action<int> OnRoundStarted;
    public float DeathReplaySeconds => Session.ArenaReplaySelection.Seconds;
    public bool HoldRespawn => HasResult;
    // Terminal results also suppress the ambient population, whose admission
    // path asks this property rather than assuming only custom spawners use it.
    public bool ControlsOwnSpawns => HasResult;
    internal bool HasResult => _rules?.Phase == DeathmatchPhase.Finished;
    private bool WaitingForStart => _rules?.Phase == DeathmatchPhase.Waiting;
    internal bool ResultOwnsCurrentRaid => HasResult && IsCurrentRaid();
    internal string ResultHeading => _rules?.Outcome switch
    {
        DeathmatchOutcome.Won => "VICTORY",
        DeathmatchOutcome.Lost => "DEFEAT",
        DeathmatchOutcome.Draw => "DRAW",
        DeathmatchOutcome.NoContest => "NO CONTEST",
        _ => "MATCH COMPLETE"
    };
    internal string ResultDetail => (_rules?.MatchWinnerName != null ? _rules.MatchWinnerName + " wins — " : "")
        + $"{RoundsWon} rounds won / {RoundsLost} lost — {_rules?.Reason}";
    internal string ResultExitStatus => _resultExitProblem ?? (TdmMatchExit.PipelineRunning
        ? "Finishing player replacement…" : $"Returning to menu in {ResultDisplayRemaining}s");
    internal int ResultDisplayRemaining => _resultDisplayAt < 0f ? (int)DeathmatchRules.ResultSeconds
        : Math.Max(0, (int)Math.Ceiling(DeathmatchRules.ResultSeconds - (Time.unscaledTime - _resultDisplayAt)));
    public string RoundResultLine => Round.IsDraw ? $"Round {Round.Number}: draw — {Round.EndReason}"
        : Round.WinnerName != null ? $"Round {Round.Number}: {Round.WinnerName} wins — {Round.EndReason}"
        : $"Round {Round.Number}: {(_rules?.RoundOutcome == DeathmatchOutcome.Lost ? "target missed" : "complete")} — {Round.EndReason}";

    public string HudStatusLine
    {
        get
        {
            if (HasResult) return ResultHeading + " — " + ResultDetail;
            if (WaitingForStart) return "PREPARING DEATHMATCH — equipping player";
            if (InIntermission) return RoundResultLine + $" — next in {Mathf.CeilToInt(IntermissionRemaining)}s";
            string line = Continuous ? "Continuous Deathmatch" : "Round " + Round.Number + (RoundLimit > 0 ? "/" + RoundLimit : "");
            line += FreeForAll ? " | Free-for-all" : " | All vs player";
            if (KillTarget > 0) line += $" | {Round.Kills}/{KillTarget} kills";
            float remaining = TimeRemaining;
            if (remaining >= 0f) line += $" | {Mathf.FloorToInt(remaining / 60f):0}:{Mathf.FloorToInt(remaining % 60f):00}";
            return line;
        }
    }

    public void OnMatchStart(ArenaMatch match)
    {
        if (_match != null) throw new InvalidOperationException("A Deathmatch instance belongs to one raid.");
        _match = match ?? throw new ArgumentNullException(nameof(match));
        _game = Singleton<AbstractGame>.Instance as LocalGame;
        _world = Singleton<GameWorld>.Instance;
        _localId = _game?.ProfileId;
        if (!IsCurrentRaid() || string.IsNullOrWhiteSpace(_localId)
            || _world.MainPlayer?.ProfileId != _localId)
            throw new InvalidOperationException("Deathmatch requires the exact active local raid and player identity.");
        _rules = new DeathmatchRules(_options, _localId, match.Scoreboard.Local.DisplayName);
        // ArenaSession schedules its initial kit swap after OnMatchStart. Defer
        // readiness to Tick so that pre-pipeline loading can register its lease.
    }

    public void Tick(float deltaTime)
    {
        if (!IsCurrentRaid()) return;
        if (HasResult) { TickResult(); return; }
        if (WaitingForStart)
        {
            if (Respawn.LiveKitRespawn.IsInitialSwapPending || Respawn.ArenaRespawnDriver.IsRespawnInProgress
                || TdmMatchExit.PipelineRunning || _world.MainPlayer?.HealthController?.IsAlive != true
                || _world.MainPlayer.ProfileId != _localId || !ReferenceEquals(_game.LocalPlayer, _world.MainPlayer)) return;
            StartRound();
            return;
        }
        if (InIntermission)
        {
            if (Respawn.ArenaRespawnDriver.IsRespawnInProgress || TdmMatchExit.PipelineRunning) return;
            if (_breakRespawnPending)
            {
                if (Time.time < _intermissionEndsAt + StuckRespawnGraceSeconds) return;
                Plugin.ModLogger.LogWarning("Arena round break respawn never reported back — starting the next round without it.");
                _breakRespawnPending = false;
                // Invalidate the old coroutine's predicate and its eventual
                // callback even if it wakes during a later intermission.
                _breakGeneration++;
            }
            if (Time.time >= _intermissionEndsAt) StartRound();
            return;
        }
        _rules.Tick(Time.time);
        PublishRoundEnd();
    }

    internal bool TryBeginScoredEvent()
    {
        if (!IsCurrentRaid() || _rules == null) return false;
        bool accepted = _rules.AdmitEvent(Time.time);
        PublishRoundEnd();
        return accepted;
    }

    internal bool TryRecordLocalDeath()
    {
        if (_localDeathCounted || !TryBeginScoredEvent()) return false;
        _localDeathCounted = true;
        return true;
    }

    public void OnKill(ArenaKill kill)
    {
        if (kill == null || !IsCurrentRaid() || _rules?.Phase != DeathmatchPhase.Live || !kill.ScoreEligible) return;
        _rules.RecordKill(Time.time, kill.KillerProfileId, kill.KillerName, kill.VictimProfileId);
        UpdateRoundState();
        PublishRoundEnd();
    }

    public void OnLocalDeath()
    {
        if (!IsCurrentRaid() || _rules?.Phase != DeathmatchPhase.Live) return;
        _rules.RecordLocalDeath(Time.time);
        UpdateRoundState();
        PublishRoundEnd();
    }

    public void OnLocalRespawn(Player player)
    {
        if (IsCurrentRaid() && !HasResult && player != null && player.ProfileId == _localId
            && ReferenceEquals(_world.MainPlayer, player)) _localDeathCounted = false;
    }

    public void OnMatchEnd()
    {
        _ended = true;
        _breakGeneration++;
        _breakRespawnPending = false;
        _rules?.Stop();
    }

    internal bool ShouldBlockMatchDamage(Player victim)
        => IsCurrentRaid() && (HasResult || WaitingForStart) && victim != null && victim.ProfileId == _localId
            && ReferenceEquals(_world.MainPlayer, victim);

    internal bool TryGetMatchNotice(out string heading, out string detail)
    {
        heading = detail = null;
        if (!IsCurrentRaid() || (!HasResult && !WaitingForStart)) return false;
        heading = HasResult ? ResultHeading : "PREPARING DEATHMATCH";
        detail = HasResult ? ResultDetail + " | " + ResultExitStatus : "Equipping player — round time and scoring start when ready";
        return true;
    }

    internal void RequestResultExit() { if (ResultOwnsCurrentRaid) _resultExitRequested = true; }

    internal void ReportResultExitProblem(string problem)
    {
        if (!ResultOwnsCurrentRaid || string.Equals(problem, _resultExitProblem, StringComparison.Ordinal)) return;
        _resultExitProblem = problem;
        Plugin.ModLogger.LogWarning("Deathmatch result: " + problem);
    }

    private bool IsCurrentRaid()
        => !_ended && _match != null && _game != null && _game.Status == GameStatus.Started && _world != null
            && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
            && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
            && ArenaSession.IsExactActiveRaid(_game, _world)
            && ReferenceEquals(ArenaMatchService.Current, _match);

    private void TickResult()
    {
        if (!ResultOwnsCurrentRaid || TdmMatchExit.PipelineRunning) return;
        if (_resultDisplayAt < 0f) _resultDisplayAt = Time.unscaledTime;
        if (!_resultExitRequested && ResultDisplayRemaining > 0) return;
        if (Time.unscaledTime < _nextStopAttempt) return;
        _nextStopAttempt = Time.unscaledTime + 5f;
        TdmMatchExit.TryStop(_game, _world, _localId, ReportResultExitProblem);
    }

    private void UpdateRoundState()
    {
        Round.Kills = _rules.LocalKills;
        Round.Deaths = _rules.LocalDeaths;
        Round.Score = _match.Score - _scoreAtRoundStart;
        Round.WinnerProfileId = _rules.WinnerProfileId;
        Round.WinnerName = _rules.WinnerName;
        Round.EndReason = _rules.RoundReason;
        Round.IsDraw = _rules.RoundOutcome == DeathmatchOutcome.Draw;
    }

    private void PublishRoundEnd()
    {
        if (_rules == null || (_rules.Phase != DeathmatchPhase.Intermission && !HasResult)
            || _publishedRound == Round.Number) return;
        _publishedRound = Round.Number;
        UpdateRoundState();
        if (!HasResult) BeginIntermission();
        Plugin.ModLogger.LogInfo($"Arena {RoundResultLine}; {Round.Kills} kills / {Round.Deaths} deaths / {Round.Score} points.");
        ArenaToasts.Show(HasResult ? ResultHeading + " — " + ResultDetail : RoundResultLine);
        if (HasResult)
        {
            if (_rules.Outcome == DeathmatchOutcome.Won) _match.ReportAnnouncerCue(ArenaAnnouncerCue.MatchWon);
            else if (_rules.Outcome == DeathmatchOutcome.Lost) _match.ReportAnnouncerCue(ArenaAnnouncerCue.MatchLost);
            else if (_rules.Outcome == DeathmatchOutcome.NoContest) _match.ReportAnnouncerCue(ArenaAnnouncerCue.NoContest);
        }
        try
        {
            // Publish a snapshot: a subscriber holding a finished round must
            // not see it mutate into the next round's zeroed scoring bracket.
            OnRoundEnded?.Invoke(new RoundState { Number = Round.Number, Kills = Round.Kills,
                Deaths = Round.Deaths, Score = Round.Score, StartedAt = Round.StartedAt,
                WinnerProfileId = Round.WinnerProfileId, WinnerName = Round.WinnerName,
                EndReason = Round.EndReason, IsDraw = Round.IsDraw });
        }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena round-end subscriber failed: {ex}"); }
    }

    private void BeginIntermission()
    {
        float duration = Mathf.Max(3f, _options.IntermissionSeconds);
        _intermissionEndsAt = Time.time + duration;
        int generation = ++_breakGeneration;
        // Set before scheduling: a native coroutine can complete synchronously.
        _breakRespawnPending = _options.Respawn;
        bool respawning = false;
        try
        {
            respawning = _options.Respawn && IsCurrentRaid()
                && Respawn.LiveKitRespawn.AtRoundBreak(_game, duration,
                    () => IsCurrentRaid() && InIntermission && generation == _breakGeneration,
                    success => OnRoundBreakRespawn(generation, success));
        }
        catch (Exception ex)
        {
            // A rejected native request must not strand the pending latch or
            // suppress the scored round event that follows this operation.
            Plugin.ModLogger.LogWarning($"Arena round respawn request failed: {ex.Message}");
        }
        if (!respawning)
        {
            _breakRespawnPending = false;
            HealForNewRound();
            if (!ArenaProtection.Begin(duration))
                Plugin.ModLogger.LogWarning("Arena intermission without protection — input stays free.");
        }
    }

    private void OnRoundBreakRespawn(int generation, bool success)
    {
        if (generation != _breakGeneration || !IsCurrentRaid() || !InIntermission) return;
        _breakRespawnPending = false;
        float duration = Mathf.Max(3f, _options.IntermissionSeconds);
        _intermissionEndsAt = Time.time + duration;
        if (success) return;
        HealForNewRound();
        ArenaProtection.Begin(duration);
    }

    private void StartRound()
    {
        if (!_rules.StartRound(Time.time)) { PublishRoundEnd(); return; }
        _breakRespawnPending = false;
        _breakGeneration++;
        Round.Number = _rules.RoundNumber;
        Round.StartedAt = Time.time;
        _scoreAtRoundStart = _match.Score;
        UpdateRoundState();
        _match.BeginAwardRound(clearRevenge: false);
        Plugin.ModLogger.LogInfo($"Arena Deathmatch round {Round.Number} started.");
        if (Round.Number > 1) ArenaToasts.Show($"Round {Round.Number} — fight!");
        try { OnRoundStarted?.Invoke(Round.Number); }
        catch (Exception ex) { Plugin.ModLogger.LogWarning($"Arena round-start subscriber failed: {ex}"); }
    }
    private static void HealForNewRound()
    {
        try
        {
            var player = Comfort.Common.Singleton<EFT.GameWorld>.Instantiated
                ? Comfort.Common.Singleton<EFT.GameWorld>.Instance.MainPlayer
                : null;

            var health = player?.ActiveHealthController;
            if (health == null || player.HealthController == null || !player.HealthController.IsAlive)
                return;

            if (Respawn.ArenaRespawnDriver.IsRespawnInProgress)
                return;

            health.RestoreFullHealth();
            health.RemoveNegativeEffects(EBodyPart.Common);

            // Intoxication is permanent and survives the line above.
            health.RemoveIntoxication();

            // Both clamp at their maximum.
            health.ChangeEnergy(1000f);
            health.ChangeHydration(1000f);

            // The full-heal path writes the body parts directly and raises
            // neither the restore nor the health-changed event, so the crippled
            // movement state (blacked-leg speed limit, sprint lock, arm sway)
            // would survive a "full heal" — the same trap the respawn engine
            // already documents. Drive the recompute by hand.
            player.UpdateSpeedLimitByHealth();
            player.UpdateArmsCondition();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena round heal failed: {ex.Message}");
        }
    }

}
