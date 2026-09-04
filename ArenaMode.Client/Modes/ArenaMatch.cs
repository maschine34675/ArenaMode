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

    public int Score { get; private set; }
    public int Kills { get; private set; }
    public int Deaths { get; private set; }
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }
    public int Headshots { get; private set; }

    public float StartedAt { get; }
    public float Elapsed => Time.time - StartedAt;

    private float _lastLocalKillAt = float.NegativeInfinity;
    private int _multiKillChain;

    /// <summary>Every captured kill (local and observed), after bookkeeping.</summary>
    public event Action<ArenaKill> OnKill;

    /// <summary>Local score changed (kill scored or bookkeeping reset).</summary>
    public event Action OnScoreChanged;

    /// <summary>Short shout-style label ("DOUBLE KILL", "KILLING SPREE").</summary>
    public event Action<string> OnCallout;

    /// <summary>Round lifecycle presentation that must not appear as a HUD kill callout.</summary>
    internal event Action<ArenaAnnouncerCue> OnAnnouncerCue;

    public ArenaMatch(IArenaMode mode)
    {
        Mode = mode;
        StartedAt = Time.time;
    }

    public void ReportKill(ArenaKill kill)
    {
        if (kill == null)
            return;

        if (kill.KilledByLocal && !kill.LocalIsVictim)
            ScoreLocalKill(kill);

        RaiseSafe(() => OnKill?.Invoke(kill), nameof(OnKill));
        RaiseSafe(() => Mode.OnKill(kill), "mode.OnKill");
    }

    public void ReportLocalDeath()
    {
        Deaths++;
        Streak = 0;
        _multiKillChain = 0;
        _lastLocalKillAt = float.NegativeInfinity;
        RaiseSafe(() => OnScoreChanged?.Invoke(), nameof(OnScoreChanged));
        RaiseSafe(Mode.OnLocalDeath, "mode.OnLocalDeath");
    }

    public void ReportLocalRespawn(Player player)
        => RaiseSafe(() => Mode.OnLocalRespawn(player), "mode.OnLocalRespawn");

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
        => RaiseSafe(() => OnAnnouncerCue?.Invoke(cue), nameof(OnAnnouncerCue));

    private void ScoreLocalKill(ArenaKill kill)
    {
        Kills++;
        Streak++;
        if (Streak > BestStreak)
            BestStreak = Streak;

        var points = ScoreRules.PointsFor(kill.VictimRole);
        if (kill.Headshot)
        {
            Headshots++;
            points += ScoreRules.HeadshotBonus;
        }

        if (Streak > 0 && Streak % ScoreRules.StreakBonusEvery == 0)
            points += ScoreRules.StreakBonus;

        Score += points;

        _multiKillChain = kill.Time - _lastLocalKillAt <= ScoreRules.MultiKillWindowSeconds
            ? _multiKillChain + 1
            : 1;
        _lastLocalKillAt = kill.Time;

        // ONE callout per kill — a streak milestone outranks the multi-kill
        // (both at once would cut each other's announcer stinger off).
        var label = ScoreRules.StreakLabel(Streak) ?? ScoreRules.MultiKillLabel(_multiKillChain);
        if (label != null)
            RaiseSafe(() => OnCallout?.Invoke(label), nameof(OnCallout));

        RaiseSafe(() => OnScoreChanged?.Invoke(), nameof(OnScoreChanged));
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
