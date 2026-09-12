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
    public int Streak => Scoreboard.Local.Streak;
    public int BestStreak => Scoreboard.Local.BestStreak;
    public int Headshots => Scoreboard.Local.Headshots;

    public float StartedAt { get; }
    public float Elapsed => Time.time - StartedAt;

    private float _lastLocalKillAt = float.NegativeInfinity;
    private int _multiKillChain;
    private readonly ArenaCombatAwards _awards = new();

    /// <summary>Every captured kill (local and observed), after bookkeeping.</summary>
    public event Action<ArenaKill> OnKill;

    /// <summary>Local score changed (kill scored or bookkeeping reset).</summary>
    public event Action OnScoreChanged;

    /// <summary>Short shout-style label ("DOUBLE KILL", "KILLING SPREE").</summary>
    public event Action<string> OnCallout;

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
            if (!teams.TryBeginScoredEvent()) return;
            teams.ClassifyKill(kill);
            // Native death and kill notifications can arrive in either order.
            // Count the final local casualty before the terminal score freezes;
            // Team DM deduplicates the later driver's event for the same life.
            if (kill.LocalIsVictim) ReportLocalDeath();
        }
        if (Mode is RoundsMode deathmatch)
        {
            if (!deathmatch.TryBeginScoredEvent()) return;
            if (kill.LocalIsVictim) ReportLocalDeath();
        }
        Scoreboard.ReportKill(kill);

        string existingLabel = null;
        if (kill.KilledByLocal && !kill.LocalIsVictim && kill.AwardEligible)
        {
            _multiKillChain = kill.Time - _lastLocalKillAt <= ScoreRules.MultiKillWindowSeconds
                ? _multiKillChain + 1 : 1;
            _lastLocalKillAt = kill.Time;
            existingLabel = ScoreRules.StreakLabel(Streak) ?? ScoreRules.MultiKillLabel(_multiKillChain);
        }
        var label = _awards.ReportKill(kill, existingLabel);
        if (label != null)
            RaiseSafe(() => OnCallout?.Invoke(label), nameof(OnCallout));
        if (kill.KilledByLocal && !kill.LocalIsVictim)
            RaiseSafe(() => OnScoreChanged?.Invoke(), nameof(OnScoreChanged));

        RaiseSafe(() => OnKill?.Invoke(kill), nameof(OnKill));
        RaiseSafe(() => Mode.OnKill(kill), "mode.OnKill");
    }

    public void ReportLocalDeath()
    {
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

    public void ReportLocalRespawn(Player player)
    {
        if (ArenaMatchResult.HasResult(Mode)) return;
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
        => RaiseSafe(Mode.OnMatchEnd, "mode.OnMatchEnd");

    internal void ReportAnnouncerCue(ArenaAnnouncerCue cue)
    {
        if (cue == ArenaAnnouncerCue.RoundStart) BeginAwardRound();
        RaiseSafe(() => OnAnnouncerCue?.Invoke(cue), nameof(OnAnnouncerCue));
    }

    internal void BeginAwardRound(bool clearRevenge = true)
    {
        _awards.BeginRound(clearRevenge);
        _multiKillChain = 0;
        _lastLocalKillAt = float.NegativeInfinity;
    }

    internal void ReportTeamSurvivors(bool localAlive, int alliesAlive, int enemiesAlive)
    {
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
