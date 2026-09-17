using ArenaMode.Combat;
using EFT;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// The per-raid match: scoring, streak/multi-kill bookkeeping and the event
/// surface the HUD and announcer feed from. Owned by ArenaMatchService; exactly
/// one instance per arena raid. Kill callouts and non-HUD lifecycle cues are
/// raised as events so audio and lighting presentation stay out of the rules.
/// </summary>
public sealed class ArenaMatch
{
    public IArenaMode Mode { get; }

    public ArenaScoreboard Scoreboard { get; }

    // Existing consumers keep one source of truth: the local scoreboard row.
    public int Score => Scoreboard.Local.Score;
    public int Kills => Scoreboard.Local.Kills;
    public int Deaths => Scoreboard.Local.Deaths;
    public int Assists => Scoreboard.Local.Assists;
    public int Streak => Scoreboard.Local.Streak;
    public int BestStreak => Scoreboard.Local.BestStreak;
    public int Headshots => Scoreboard.Local.Headshots;

    public float StartedAt { get; }
    public float Elapsed => Time.time - StartedAt;

    private float _lastLocalKillAt = float.NegativeInfinity;
    private int _multiKillChain;
    private readonly ArenaCombatAwards _awards = new();
    private bool _noContestAnnounced;

    /// <summary>Every captured kill (local and observed), after bookkeeping.</summary>
    public event Action<ArenaKill> OnKill;

    /// <summary>One proportional contribution reward; never a kill or speech event.</summary>
    public event Action<ArenaAssist> OnAssist;

    /// <summary>Local score changed (kill, assist or bookkeeping reset).</summary>
    public event Action OnScoreChanged;

    /// <summary>Short shout-style label ("DOUBLE KILL", "KILLING SPREE").</summary>
    public event Action<string> OnCallout;

    /// <summary>HUD only; no audio cue, score event or speech queue entry.</summary>
    internal event Action<string> OnTextCallout;

    internal void ReportTextCallout(string label)
        => RaiseSafe(() => OnTextCallout?.Invoke(label), nameof(OnTextCallout));

    /// <summary>Round lifecycle presentation that must not appear as a HUD kill callout.</summary>
    internal event Action<ArenaAnnouncerCue> OnAnnouncerCue;

    public ArenaMatch(
        IArenaMode mode,
        string localProfileId = null,
        string localDisplayName = null,
        string localRoleLabel = null)
    {
        Mode = mode;
        Scoreboard = new ArenaScoreboard(localProfileId, localDisplayName, localRoleLabel);
        StartedAt = Time.time;
    }

    public void ReportKill(ArenaKill kill)
    {
        if (kill == null)
            return;

        if (Mode is TeamDeathmatchMode teams)
        {
            if (!teams.TryBeginScoredEvent())
            {
                ForgetAssistVictim(kill.VictimBodyId);
                return;
            }
            teams.ClassifyKill(kill);
            // Native death and kill notifications can arrive in either order.
            // Count the final local casualty before the terminal score freezes;
            // Team DM deduplicates the later driver's event for the same life.
            if (kill.LocalIsVictim) ReportLocalDeath();
        }
        if (Mode is RoundsMode deathmatch)
        {
            if (!deathmatch.TryBeginScoredEvent())
            {
                ForgetAssistVictim(kill.VictimBodyId);
                return;
            }
            if (kill.LocalIsVictim) ReportLocalDeath();
        }
        // ReportKill clears the victim's life. Capture only its currently bound,
        // opposing physical body before that reset; historical aliases do not count.
        bool shutdown = Scoreboard.IsShutdown(kill);
        Scoreboard.ReportKill(kill);
        foreach (var assist in kill.Assists)
        {
            RaiseSafe(() => OnAssist?.Invoke(assist), nameof(OnAssist));
            if (assist.IsLocal)
                RaiseSafe(() => OnScoreChanged?.Invoke(), nameof(OnScoreChanged));
        }

        string existingLabel = null;
        if (kill.KilledByLocal && !kill.LocalIsVictim && kill.AwardEligible)
        {
            _multiKillChain = kill.Time - _lastLocalKillAt <= ScoreRules.MultiKillWindowSeconds
                ? _multiKillChain + 1 : 1;
            _lastLocalKillAt = kill.Time;
            existingLabel = ScoreRules.MultiKillLabel(_multiKillChain) ?? ScoreRules.StreakLabel(Streak);
        }
        var label = _awards.ReportKill(kill, shutdown ? "SHUTDOWN" : existingLabel);
        if (label != null)
            RaiseSafe(() => OnCallout?.Invoke(label), nameof(OnCallout));
        if (kill.KilledByLocal && !kill.LocalIsVictim)
            RaiseSafe(() => OnScoreChanged?.Invoke(), nameof(OnScoreChanged));

        RaiseSafe(() => OnKill?.Invoke(kill), nameof(OnKill));
        RaiseSafe(() => Mode.OnKill(kill), "mode.OnKill");
    }

    public void ReportLocalDeath()
    {
        _awards.InvalidateClutch();
        if (Mode is TeamDeathmatchMode teams && !teams.TryRecordLocalDeath())
        {
            // Unscored staging deaths still invalidate the current countdown.
            RaiseSafe(teams.OnLocalDeath, "mode.OnLocalDeath");
            return;
        }
        if (Mode is RoundsMode deathmatch && !deathmatch.TryRecordLocalDeath()) return;
        Scoreboard.ReportLocalDeath();
        _awards.LocalDeath();
        _multiKillChain = 0;
        _lastLocalKillAt = float.NegativeInfinity;
        RaiseSafe(() => OnScoreChanged?.Invoke(), nameof(OnScoreChanged));
        RaiseSafe(Mode.OnLocalDeath, "mode.OnLocalDeath");
    }

    internal void RecordAssistDamage(ArenaAssistDamage damage)
    {
        if (Mode == null || Mode.IsComplete) return;
        Scoreboard.RecordAssistDamage(damage);
    }

    internal void ForgetAssistVictim(long bodyId) => Scoreboard.ForgetAssistVictim(bodyId);

    public void ReportLocalRespawn(Player player)
    {
        if (ArenaMatchResult.HasResult(Mode)) return;
        // A replacement body never inherits a surviving last-man win, including
        // an unexpected respawn within a protected round.
        _awards.InvalidateClutch();
        // Isolated like the mode callback: a scoreboard fault must not keep the mode
        // from learning about the new body.
        RaiseSafe(() => Scoreboard.ReportLocalRespawn(
            player?.ProfileId,
            RoleClassifier.DisplayName(player),
            RoleClassifier.Label(player)), "scoreboard.ReportLocalRespawn");
        RaiseSafe(() => Mode.OnLocalRespawn(player), "mode.OnLocalRespawn");
    }

    public void Tick(float deltaTime)
    {
        // Direct call — the closure form would allocate every frame.
        try
        {
            Mode.Tick(deltaTime);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena match event mode.Tick failed: {ex}");
        }
    }

    public void End()
    {
        Scoreboard.ResetAssists();
        RaiseSafe(Mode.OnMatchEnd, "mode.OnMatchEnd");
    }

    internal void ReportAnnouncerCue(ArenaAnnouncerCue cue, bool beginAwardRound = true)
    {
        // A technical terminal outcome may be reported by several teardown
        // paths. It is presentation only, once per match, never a scored result.
        if (cue == ArenaAnnouncerCue.NoContest)
        {
            if (_noContestAnnounced) return;
            _noContestAnnounced = true;
            _awards.InvalidateClutch();
        }
        // Solo Tactical resets before dispatch: early kills may precede the
        // audible readiness cue, which must not erase their awards or chain.
        if (cue == ArenaAnnouncerCue.RoundStart && beginAwardRound) BeginAwardRound();
        if (cue is ArenaAnnouncerCue.RoundLost or ArenaAnnouncerCue.MatchLost or ArenaAnnouncerCue.Draw)
            _awards.InvalidateClutch();
        if (cue is ArenaAnnouncerCue.RoundWon or ArenaAnnouncerCue.MatchWon)
        {
            bool clutch = _awards.ConsumeClutch();
            if (clutch && Mode is TacticalMode tactical && tactical.IsTeamArena)
                RaiseSafe(() => OnAnnouncerCue?.Invoke(ArenaAnnouncerCue.Clutch), nameof(OnAnnouncerCue));
        }
        // Preserve the original result event for lighting and every other
        // lifecycle consumer. Audio coalesces only the immediately preceding Clutch.
        RaiseSafe(() => OnAnnouncerCue?.Invoke(cue), nameof(OnAnnouncerCue));
    }

    internal void BeginAwardRound(bool clearRevenge = true)
    {
        Scoreboard.ResetAssists();
        ArenaShotAwardHistory.Reset();
        _awards.BeginRound(clearRevenge);
        _multiKillChain = 0;
        _lastLocalKillAt = float.NegativeInfinity;
    }

    internal void ReportTeamSurvivors(bool localAlive, int alliesAlive, int enemiesAlive)
    {
        if (!(Mode is TacticalMode tactical) || !tactical.IsTeamArena) return;
        if (_awards.LastManStanding(localAlive, alliesAlive, enemiesAlive))
            ReportAnnouncerCue(ArenaAnnouncerCue.LastManStanding);
    }

    private static void RaiseSafe(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena match event {name} failed: {ex}");
        }
    }
}
