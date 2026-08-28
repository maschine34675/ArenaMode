using ArenaMode.Combat;
using ArenaMode.Config;
using System;
using UnityEngine;

namespace ArenaMode.Modes;

/// <summary>
/// Rounds: play until a kill target or a time limit is reached, show the
/// scoreboard during a short intermission, then start the next round inside the
/// SAME raid. Nothing about the world is reset — bots keep fighting, the map
/// stays as it is; a round is a scoring bracket, not a map restart. That keeps
/// the mode honest with everything we learned about mid-raid teardown.
/// </summary>
public sealed class RoundsMode : IArenaMode
{
    /// <summary>Round bookkeeping, deliberately separate from the match totals.</summary>
    public sealed class RoundState
    {
        public int Number = 1;
        public int Kills;
        public int Deaths;
        public int Score;
        public float StartedAt;
    }

    private ArenaMatch _match;
    private float _intermissionEndsAt;
    private int _killsAtRoundStart;
    private int _deathsAtRoundStart;
    private int _scoreAtRoundStart;

    public string Id => "rounds";

    public string DisplayName => "Rounds";

    /// <summary>The match never "completes" — rounds keep coming until the raid ends.</summary>
    public bool IsComplete => false;

    public RoundState Round { get; } = new RoundState();

    /// <summary>True while the scoreboard shows and the next round is counting in.</summary>
    public bool InIntermission { get; private set; }

    /// <summary>
    /// True only while the player is actually invulnerable for the pause. The
    /// panel freezes input during an intermission, so it must not do that when
    /// protection was refused — a frozen, vulnerable player with every bot
    /// hunting them is worse than no scoreboard at all.
    /// </summary>
    public bool IntermissionProtected { get; private set; }

    public float IntermissionRemaining =>
        InIntermission ? Mathf.Max(0f, _intermissionEndsAt - Time.time) : 0f;

    /// <summary>Seconds left in the round, or -1 when the mode runs on kills only.</summary>
    public float TimeRemaining
    {
        get
        {
            var limit = Settings.RoundTimeLimitSeconds.Value;
            if (limit <= 0)
                return -1f;

            return Mathf.Max(0f, Round.StartedAt + limit - Time.time);
        }
    }

    public int KillTarget => Settings.RoundKillTarget.Value;

    /// <summary>Raised when a round ends (arg: the finished round's state).</summary>
    public event Action<RoundState> OnRoundEnded;

    public event Action<int> OnRoundStarted;

    public string HudStatusLine
    {
        get
        {
            if (InIntermission)
                return $"Round {Round.Number} over — next in {Mathf.CeilToInt(IntermissionRemaining)}s";

            var parts = $"Round {Round.Number}";
            var target = KillTarget;
            if (target > 0)
                parts += $"   {Round.Kills}/{target} kills";

            var remaining = TimeRemaining;
            if (remaining >= 0f)
                parts += $"   {Mathf.FloorToInt(remaining / 60f):0}:{Mathf.FloorToInt(remaining % 60f):00}";

            return parts;
        }
    }

    public void OnMatchStart(ArenaMatch match)
    {
        _match = match;

        if (KillTarget <= 0 && Settings.RoundTimeLimitSeconds.Value <= 0)
        {
            Plugin.ModLogger.LogWarning(
                "Rounds mode has neither a kill target nor a time limit — the round will never end. "
                + "Set RoundKillTarget or RoundTimeLimitSeconds in the config.");
        }

        StartRound(1);
    }

    public void Tick(float deltaTime)
    {
        if (_match == null)
            return;

        if (InIntermission)
        {
            if (Time.time >= _intermissionEndsAt)
                StartRound(Round.Number + 1);

            return;
        }

        if (IsRoundOver())
            EndRound();
    }

    public void OnKill(ArenaKill kill)
    {
        if (_match == null || InIntermission || !kill.KilledByLocal || kill.LocalIsVictim)
            return;

        Round.Kills = _match.Kills - _killsAtRoundStart;
        Round.Score = _match.Score - _scoreAtRoundStart;
    }

    public void OnLocalDeath()
    {
        // Symmetric with OnKill: what happens during the pause belongs to the
        // next round, whose snapshot is taken when it starts.
        if (_match == null || InIntermission)
            return;

        Round.Deaths = _match.Deaths - _deathsAtRoundStart;
    }

    public void OnLocalRespawn()
    {
    }

    public void OnMatchEnd()
    {
        EndRound(matchEnding: true);
    }

    private bool IsRoundOver()
    {
        var target = KillTarget;
        if (target > 0 && Round.Kills >= target)
            return true;

        var remaining = TimeRemaining;
        return remaining >= 0f && remaining <= 0f;
    }

    private void EndRound(bool matchEnding = false)
    {
        if (InIntermission)
            return;

        InIntermission = true;
        var duration = Mathf.Max(3f, Settings.RoundIntermissionSeconds.Value);
        _intermissionEndsAt = Time.time + duration;

        // Nobody should lose a round on the scoreboard screen — but not while
        // the raid is tearing down: the protection coroutine has no teardown
        // guard and would be left holding a zeroed damage coefficient.
        IntermissionProtected = !matchEnding && Combat.ArenaProtection.Begin(duration);
        if (!matchEnding && !IntermissionProtected)
            Plugin.ModLogger.LogWarning("Arena intermission without protection — input stays free.");

        Plugin.ModLogger.LogInfo(
            $"Arena round {Round.Number} ended: {Round.Kills} kills / {Round.Deaths} deaths, score {Round.Score}.");
        ArenaToasts.Show($"Round {Round.Number}: {Round.Kills} kills, {Round.Score:n0} points");

        try
        {
            OnRoundEnded?.Invoke(Round);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena round-end subscriber failed: {ex}");
        }
    }

    /// <summary>
    /// Every round starts from a clean slate. Only the player's condition is
    /// touched: the world, the bots and the inventory stay exactly as they are
    /// — a round is a scoring bracket, not a map restart. A player who died
    /// during the intermission is skipped; the respawn driver owns them and has
    /// already healed and protected them.
    /// </summary>
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

    private void StartRound(int number)
    {
        InIntermission = false;
        IntermissionProtected = false;
        Round.Number = number;
        Round.Kills = 0;
        Round.Deaths = 0;
        Round.Score = 0;
        Round.StartedAt = Time.time;

        _killsAtRoundStart = _match?.Kills ?? 0;
        _deathsAtRoundStart = _match?.Deaths ?? 0;
        _scoreAtRoundStart = _match?.Score ?? 0;

        Plugin.ModLogger.LogInfo($"Arena round {number} started.");
        if (number > 1)
        {
            HealForNewRound();
            ArenaToasts.Show($"Round {number} — fight!");
        }

        try
        {
            OnRoundStarted?.Invoke(number);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena round-start subscriber failed: {ex}");
        }
    }
}
